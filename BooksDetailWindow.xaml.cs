using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using FirebirdSql.Data.FirebirdClient;

namespace ukol1
{
    public partial class BooksDetailWindow : Window
    {
        private const string SqlGenres = @"
            SELECT z.NAZEV_ZANRY
            FROM KNIHY_ZANRY bg
            JOIN ZANRY z ON bg.ZANRY_ID = z.ZANRY_ID
            WHERE bg.KNIHY_ID = @id
            ORDER BY z.NAZEV_ZANRY";

        public BooksDetailWindow(int bookID)
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Loaded += async (_, __) => await LoadAsync(bookID);
        }

        //load book details and fill the UI
        private async Task LoadAsync(int id)
        {
            var d = await Manager.GetBookDetailsAsync(id);
            if (d == null)
            {
                MessageBox.Show("Kniha není v databázi.", "Chyba",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            NameText.Text = string.IsNullOrWhiteSpace(d.Nazev) ? "-" : d.Nazev;
            AuthorText.Text = string.IsNullOrWhiteSpace(d.Autor) ? "-" : d.Autor;
            YearTex.Text = d.Rok?.ToString() ?? "-";
            NumberOfPagesText.Text = d.PocetStran?.ToString() ?? "-";
            PublisherText.Text = string.IsNullOrWhiteSpace(d.Nakladatelstvi) ? "-" : d.Nakladatelstvi;
            DescText.Text = string.IsNullOrWhiteSpace(d.PopisKnihy) ? "-" : d.PopisKnihy;

            //cover image
            LoadCover(Manager.ToAbsolute(d.KnihyCesta));

            //genres
            try
            {
                using var conn = new FbConnection(Manager.ConnectionString);
                await conn.OpenAsync();

                using var cmd = new FbCommand(SqlGenres, conn);
                cmd.Parameters.AddWithValue("id", id);

                var names = new List<string>();
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    names.Add(await rdr.IsDBNullAsync(0)
                        ? ""
                        : await rdr.GetFieldValueAsync<string>(0));
                }

                //show "-" if all entries are empty/whitespace
                GenreText.Text = names.Any(s => !string.IsNullOrWhiteSpace(s))
                    ? string.Join(", ", names.Where(s => !string.IsNullOrWhiteSpace(s)))
                    : "-";
            }
            catch
            {
                GenreText.Text = "-";
            }
        }

        private void LoadCover(string? absPath)
        {
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
        }
    }
}