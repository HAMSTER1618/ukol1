using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using FirebirdSql.Data.FirebirdClient;
using System.Threading.Tasks;

namespace ukol1
{
    public partial class KnihyDetailWindow : Window
    {
        private readonly string _connString =
            ConfigurationManager.ConnectionStrings["KNIHOVNA2"].ConnectionString;

        public KnihyDetailWindow(int knihyID)
        {
            InitializeComponent();
            Loaded += async (_, __) => await LoadAsync(knihyID);
        }

        //nacteni detailu knihy + naplneni UI
        private async Task LoadAsync(int id)
        {
            // nacteni hlavniho detailu knihy pres servis MvvmBD (async)
            var d = await MvvmBD.GetBookDetailsAsync(_connString, id);
            if (d == null)
            {
                MessageBox.Show("Kniha není v databázi.", "Chyba",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            // vyplneni UI (vyuzijeme '-' pro prazdne hodnoty)
            NazevText.Text = string.IsNullOrWhiteSpace(d.Nazev) ? "-" : d.Nazev;
            AutorText.Text = string.IsNullOrWhiteSpace(d.Autor) ? "-" : d.Autor;
            RokText.Text = d.Rok?.ToString() ?? "-";
            PocetStranText.Text = d.PocetStran?.ToString() ?? "-";
            NakladatelText.Text = string.IsNullOrWhiteSpace(d.Nakladatelstvi) ? "-" : d.Nakladatelstvi;
            PopisText.Text = string.IsNullOrWhiteSpace(d.PopisKnihy) ? "-" : d.PopisKnihy;

            // nacteni obalky (pokud je cesta)
            var absPath = MvvmBD.ToAbsolute(d.KnihyCesta);
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
                CoverImage.Source = null;
            }

            // nacteni zanru
            const string sqlZanry = @"
                    SELECT z.NAZEV_ZANRY
                    FROM KNIHY_ZANRY bg
                    JOIN ZANRY z ON bg.ZANRY_ID = z.ZANRY_ID
                    WHERE bg.KNIHY_ID = @id
                    ORDER BY z.NAZEV_ZANRY";

            try
            {
                using var conn = new FbConnection(_connString);
                await conn.OpenAsync();
                using var cmd = new FbCommand(sqlZanry, conn);
                cmd.Parameters.AddWithValue("id", id);

                var names = new List<string>();
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    names.Add(rdr.IsDBNull(0) ? "" : rdr.GetString(0));

                ZanryText.Text = names.Any()
                    ? string.Join(", ", names.Where(s => !string.IsNullOrWhiteSpace(s)))
                    : "-";
            }
            catch
            {
                // pri chybe aspon zobrazime '-'
                ZanryText.Text = "-";
            }
        }
    }
}