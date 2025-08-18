using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using FirebirdSql.Data.FirebirdClient;

namespace ukol1
{
    public partial class KnihyDetailWindow : Window
    {
        private readonly string _connString =
            ConfigurationManager.ConnectionStrings["KNIHOVNA2"].ConnectionString;

        public KnihyDetailWindow(int knihyID)
        {
            InitializeComponent();
            LoadKnihyDetail(knihyID);
        }

        //pomocny: relativni -> absolutni cesta (pokud je treba)
        private static string? ToAbsolute(string? dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath)) return null;
            return Path.IsPathRooted(dbPath)
                ? dbPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
        }

        //nacteni detailu knihy + naplneni UI
        private void LoadKnihyDetail(int id)
        {
            const string sqlMain = @"
                SELECT
                  k.NAZEV,
                  k.ROK,
                  k.POCET_STRAN,
                  TRIM(COALESCE(a.PRIJMENI,'') || ' ' || COALESCE(a.JMENO,'')) AS AUTOR,
                  TRIM(COALESCE(n.NAZEV_FIRMY,'')) || ', ' || TRIM(COALESCE(n.MESTO,'')) AS NAKLADATELSTVI,
                  COALESCE(d.POPIS_KNIHY,'')  AS POPIS_KNIHY,
                  COALESCE(d.KNIHY_CESTA,'') AS KNIHY_CESTA
                FROM KNIHY k
                LEFT JOIN AUTORY a ON k.AUTOR_ID = a.ID
                LEFT JOIN NAKLADATELSTVI n ON k.NAKLADATELSTVI_ID = n.ID
                LEFT JOIN KNIHY_DETAILY d ON k.ID = d.KNIHYDET_ID
                WHERE k.ID = @id";

            using (var conn = new FbConnection(_connString))
            using (var cmd = new FbCommand(sqlMain, conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                conn.Open();

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    //UI text s diakritikou
                    MessageBox.Show("Kniha není v databázi.", "Chyba",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                //titulek
                NazevText.Text = reader.IsDBNull(0) ? "-" : reader.GetString(0);

                //autor
                var autor = reader.IsDBNull(3) ? "" : reader.GetString(3);
                AutorText.Text = string.IsNullOrWhiteSpace(autor) ? "-" : autor;

                //rok
                RokText.Text = reader.IsDBNull(1) ? "-" : reader.GetInt32(1).ToString();

                //pocet stran
                PocetStranText.Text = reader.IsDBNull(2) ? "-" : reader.GetInt32(2).ToString();

                //nakladatelstvi (v SQL je vzdy retezec, klidne i ", ")
                var naklad = reader.IsDBNull(4) ? "" : reader.GetString(4);
                NakladatelText.Text = string.IsNullOrWhiteSpace(naklad) ? "-" : naklad;

                //popis
                var popis = reader.IsDBNull(5) ? "" : reader.GetString(5);
                PopisText.Text = string.IsNullOrWhiteSpace(popis) ? "-" : popis;

                //obalka – nacti obrazek, pokud existuje
                var absPath = ToAbsolute(reader.IsDBNull(6) ? null : reader.GetString(6));
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
                catch
                {
                    //kdyz se nepodari, UI nepadne
                    CoverImage.Source = null;
                }
            }

            //nacteni zanru (oddeleno kvuli jednoduchosti)
            const string sqlZanry = @"
                SELECT z.NAZEV_ZANRY
                FROM KNIHY_ZANRY bg
                JOIN ZANRY z ON bg.ZANRY_ID = z.ZANRY_ID
                WHERE bg.KNIHY_ID = @id
                ORDER BY z.NAZEV_ZANRY";

            using (var conn2 = new FbConnection(_connString))
            using (var cmd2 = new FbCommand(sqlZanry, conn2))
            {
                cmd2.Parameters.AddWithValue("id", id);
                var zanry = new List<string>();
                conn2.Open();
                using var rdr2 = cmd2.ExecuteReader();
                while (rdr2.Read())
                    zanry.Add(rdr2.IsDBNull(0) ? "" : rdr2.GetString(0));

                //zobrazeni: seznam nebo "-"
                ZanryText.Text = zanry.Any()
                    ? string.Join(", ", zanry.Where(s => !string.IsNullOrWhiteSpace(s)))
                    : "-";
            }
        }
    }
}