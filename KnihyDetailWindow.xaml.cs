using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Data;
using System.IO;
using FirebirdSql.Data.FirebirdClient;
using System.Configuration;

namespace ukol1
{
    public partial class KnihyDetailWindow : Window
    {
        private readonly string _connString =
            ConfigurationManager
            .ConnectionStrings["KNIHOVNA2"]
            .ConnectionString;

        public KnihyDetailWindow(int knihyID)
        {
            InitializeComponent();
            //Loaded += MainWindow_Loaded;
            LoadKnihyDetail(knihyID);
        }

        private void LoadKnihyDetail(int id)
        {
            string sqlMain = @"
                            SELECT
                            k.NAZEV,
                            k.ROK,
                            k.POCET_STRAN,
                            TRIM(COALESCE(a.PRIJMENI,'') || ' ' || COALESCE(a.JMENO,'')) AS AUTOR,
                            TRIM(COALESCE(n.NAZEV_FIRMY,'') || ', ' || COALESCE(n.MESTO,''))  AS NAKLADATELSTVI,
                            COALESCE(d.POPIS_KNIHY,'')  AS POPIS_KNIHY,
                            COALESCE(d.KNIHY_PATH,'')   AS KNIHY_PATH
                            FROM KNIHY k
                            LEFT JOIN AUTORY a ON k.AUTOR_ID = a.ID
                            LEFT JOIN NAKLADATELSTVI n ON k.NAKLADATELSTVI_ID = n.ID
                            LEFT JOIN KNIHY_DETAILS d ON k.ID = d.KNIHYDET_ID
                            WHERE k.ID = @id";

            using var conn = new FbConnection(_connString);
            using var cmd = new FbCommand(sqlMain, conn);
            cmd.Parameters.AddWithValue("id", id);
            conn.Open();

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                MessageBox.Show("Kniha není v databázi.", "Chyba",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            //
            NazevText.Text = reader.IsDBNull(0) ? "-" : reader.GetString(0);

            //
            RokText.Text = reader.IsDBNull(1) ? "-" : reader.GetInt32(1).ToString();
            PocetStranText.Text = reader.IsDBNull(2) ? "-" : reader.GetInt32(2).ToString();

            var autor = reader.GetString(3);
            AutorText.Text = string.IsNullOrWhiteSpace(autor) ? "-" : autor;

            var naklad = reader.GetString(4);
            NakladatelText.Text = string.IsNullOrWhiteSpace(naklad) ? "-" : naklad;

            var popis = reader.GetString(5);
            PopisText.Text = string.IsNullOrWhiteSpace(popis) ? "-" : popis;

            //
            var dbPath = reader.IsDBNull(6) ? null : reader.GetString(6);
            string? absPath = null;
            if (!string.IsNullOrWhiteSpace(dbPath))
            {
                absPath = Path.IsPathRooted(dbPath)
                    ? dbPath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
            }
            try
            {
                if (!string.IsNullOrWhiteSpace(absPath) && File.Exists(absPath))
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(absPath, UriKind.Absolute);
                    img.EndInit();
                    CoverImage.Source = img;
                }
                else
                {
                    CoverImage.Source = null;
                }
            }
            catch { CoverImage.Source = null; }

            conn.Close();

            //
            string sqlGenres = @"
                            SELECT g.NAME_GENRE
                            FROM KNIHY_GENRES bg
                            JOIN GENRES g ON bg.GENRE_ID = g.GENRE_ID
                            WHERE bg.KNIHY_ID = @id
                            ORDER BY g.NAME_GENRE";

            using var conn2 = new FbConnection(_connString);
            using var cmd2 = new FbCommand(sqlGenres, conn2);
            cmd2.Parameters.AddWithValue("id", id);
            var genres = new List<string>();
            conn2.Open();
            using var rdr2 = cmd2.ExecuteReader();
            while (rdr2.Read())
                genres.Add(rdr2.IsDBNull(0) ? "" : rdr2.GetString(0));
            conn2.Close();

            GenresText.Text = genres.Any()
                ? string.Join(", ", genres.Where(s => !string.IsNullOrWhiteSpace(s)))
                : "-";
        }
    }
}