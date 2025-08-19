using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FirebirdSql.Data.FirebirdClient;

namespace ukol1
{
    public partial class KnihyDetailWindowAdd : Window
    {
        private readonly List<string> _allCities = new();
        private readonly List<string> _allGenres = new();

        private readonly string _connString =
            ConfigurationManager.ConnectionStrings["KNIHOVNA2"].ConnectionString;

        private readonly int? _knihyID;
        private int _cityIndex = -1;
        private int _genresIndex = -1;
        private string? _selectedFilePath;

        // Window for adding/editing a book
        public KnihyDetailWindowAdd(int? knihyID = null)
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            DataContext = this;

            _knihyID = knihyID;
            // UI title (keep Czech text)
            this.Title = _knihyID.HasValue ? "Upravit knihu" : "Přidání nové knihy";

            if (_knihyID.HasValue)
                LoadFields(_knihyID.Value);

            LoadSuggestions();

            // Type-ahead (publisher/city)
            PublBox.TextChanged += PublBox_TextChanged;
            PublBox.PreviewKeyDown += PublBox_PreviewKeyDown;

            // Type-ahead (genres)
            GenreBox.TextChanged += GenreBox_TextChanged;
            GenreBox.PreviewKeyDown += GenreBox_PreviewKeyDown;

            // Numeric-only fields
            HookNumericOnly(YearPrintBox);
            HookNumericOnly(NumberOfPagesBox);

            // Remove error styling when user starts typing
            NameBooksBox.TextChanged += RemoveErrorOnChange;
            AuthorBox.TextChanged += RemoveErrorOnChange;
        }

        // Root folder for cover images
        private static string CoversRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "covers");

        // UI helper: clear error styling
        private static void ClearError(Border border, TextBox tb)
        {
            border.ClearValue(BorderBrushProperty);
            border.ClearValue(BorderThicknessProperty);
            tb.ClearValue(BackgroundProperty);
        }

        // Only digits or empty string are allowed
        private static bool IsDigitsOrEmpty(string? s) => string.IsNullOrWhiteSpace(s) || s!.All(char.IsDigit);

        // Produce a file-system-safe file name
        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "cover" : cleaned;
        }

        // UI helper: mark field as invalid
        private static void MarkError(Border border, TextBox tb)
        {
            border.BorderBrush = Brushes.Red;
            border.BorderThickness = new Thickness(1.5);
            tb.Background = new SolidColorBrush(Color.FromRgb(255, 240, 240));
        }

        // Parse publisher input in the format "Company, City"
        private static void ParsePubl(string input, out string nazev, out string mesto)
        {
            nazev = ""; mesto = "";
            if (string.IsNullOrWhiteSpace(input)) return;
            var parts = input.Split(new[] { ',' }, 2, StringSplitOptions.RemoveEmptyEntries)
                             .Select(part => part.Trim())
                             .ToArray();
            if (parts.Length > 0) nazev = parts[0];
            if (parts.Length > 1) mesto = parts[1];
        }

        // Place popup under the text caret
        private static void PlacePopupUnderCaret(Popup popup, TextBox tb)
        {
            try
            {
                int i = Math.Max(0, tb.CaretIndex);
                var r = tb.GetRectFromCharacterIndex(i, true);
                popup.PlacementTarget = tb;
                popup.Placement = PlacementMode.Relative;
                popup.HorizontalOffset = r.X;
                popup.VerticalOffset = r.Y + r.Height + 2;
            }
            catch
            {
                popup.Placement = PlacementMode.Bottom;
                popup.HorizontalOffset = 0;
                popup.VerticalOffset = 0;
            }
        }

        // Split full author string into surname (last token) and given name (rest)
        private static void SplitAuthor(string full, out string prijmeni, out string jmeno)
        {
            prijmeni = ""; jmeno = "";
            if (string.IsNullOrWhiteSpace(full)) return;
            var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) { prijmeni = parts[0]; return; }
            prijmeni = parts[^1];
            jmeno = string.Join(" ", parts.Take(parts.Length - 1));
        }

        // Copy cover file to the app storage and return a DB-relative path
        private static string StoreCoverFile(string sourcePath, int bookId)
        {
            Directory.CreateDirectory(CoversRoot);

            var original = Path.GetFileName(sourcePath);
            var safe = MakeSafeFileName(original);

            var baseName = Path.GetFileNameWithoutExtension(safe);
            var ext = Path.GetExtension(safe);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            ext = ext.ToLowerInvariant();

            // Build unique name
            string targetName = $"{baseName}{bookId}{ext}";
            string targetRel = Path.Combine("covers", targetName).Replace('\\', '/');
            string targetAbs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, targetRel);

            if (File.Exists(targetAbs))
            {
                int i = 2;
                do
                {
                    targetName = $"{baseName}__{bookId}_{i}{ext}";
                    targetRel = Path.Combine("covers", targetName).Replace('\\', '/');
                    targetAbs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, targetRel);
                    i++;
                } while (File.Exists(targetAbs));
            }

            File.Copy(sourcePath, targetAbs, overwrite: true);
            return targetRel;
        }

        // Convert DB-stored relative path to absolute file system path
        private static string? ToAbsolutePath(string? dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath)) return null;
            return Path.IsPathRooted(dbPath)
                ? dbPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
        }

        // Apply selected city from the popup into publisher textbox
        private void ApplyCitySuggestions(string city)
        {
            var text = PublBox.Text ?? "";
            int sep = text.LastIndexOf(',');

            if (sep >= 0)
            {
                var head = text.Substring(0, sep).TrimEnd(' ', '\t', ',');
                PublBox.Text = string.IsNullOrWhiteSpace(head) ? city : $"{head}, {city}";
            }
            else
            {
                PublBox.Text = city;
            }

            PublBox.CaretIndex = PublBox.Text.Length;
            CityPopup.IsOpen = false;
        }

        // Apply selected genre from the popup into genre textbox
        private void ApplyGenreSuggestions(string genre)
        {
            var text = GenreBox.Text ?? "";
            int sep = text.LastIndexOf(',');
            if (sep >= 0)
            {
                var head = text.Substring(0, sep).TrimEnd(' ', '\t', ',');
                GenreBox.Text = string.IsNullOrWhiteSpace(head) ? genre : $"{head}, {genre}";
            }
            else
            {
                GenreBox.Text = genre;
            }

            // Put caret at the end and keep focus in the textbox
            GenreBox.CaretIndex = GenreBox.Text.Length;
            GenreBox.Focus();
            Keyboard.Focus(GenreBox);
            GenrePopup.IsOpen = false;
        }

        // Cancel/close the dialog
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CityList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (CityList.SelectedItem is string city)
                ApplyCitySuggestions(city);
        }

        private void GenreBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!GenrePopup.IsOpen) return;

            if (e.Key == Key.Down)
            {
                _genresIndex = Math.Min(_genresIndex + 1, GenreList.Items.Count - 1);
                GenreList.SelectedIndex = _genresIndex;
                GenreList.ScrollIntoView(GenreList.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                _genresIndex = Math.Max(_genresIndex - 1, 0);
                GenreList.SelectedIndex = _genresIndex;
                GenreList.ScrollIntoView(GenreList.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && GenreList.SelectedItem is string genre)
            {
                ApplyGenreSuggestions(genre);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                GenrePopup.IsOpen = false;
                e.Handled = true;
            }
        }

        // Type-ahead for genres
        private void GenreBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = GenreBox.Text ?? "";
            int sep = text.LastIndexOf(',');
            string token = (sep >= 0 ? text[(sep + 1)..] : text).Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                GenrePopup.IsOpen = false;
                return;
            }

            var items = _allGenres
                .Where(g => g.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .ToList();

            GenreList.ItemsSource = items;
            if (items.Count > 0)
            {
                PlacePopupUnderCaret(GenrePopup, GenreBox);
                GenrePopup.IsOpen = true;
                _genresIndex = -1;
            }
            else
            {
                GenrePopup.IsOpen = false;
            }
        }

        private void GenreList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (GenreList.SelectedItem is string genre)
                ApplyGenreSuggestions(genre);
        }

        // Hide numeric error popup for the given textbox
        private void HideNumErrorFor(TextBox tb)
        {
            if (tb == YearPrintBox) RokErrPopup.IsOpen = false;
            else if (tb == NumberOfPagesBox) StranErrPopup.IsOpen = false;
        }

        // Make a textbox accept only digits (typing and paste)
        private void HookNumericOnly(TextBox tb)
        {
            tb.PreviewTextInput += NumericOnly_PreviewTextInput;   // typing
            tb.PreviewKeyDown += NumericOnly_PreviewKeyDown;       // block Space
            DataObject.AddPastingHandler(tb, NumericOnly_OnPaste); // paste
            tb.TextChanged += RemoveErrorOnChange;                 // hide error on valid input
        }

        // Load existing values when editing
        private void LoadFields(int id)
        {
            using var conn = new FbConnection(_connString);
            conn.Open();

            using (var cmd = new FbCommand(@"
                SELECT
                    k.NAZEV,
                    k.ROK,
                    k.POCET_STRAN,
                    a.PRIJMENI,
                    a.JMENO,
                    n.NAZEV_FIRMY,
                    n.MESTO,
                    d.POPIS_KNIHY,
                    d.KNIHY_CESTA
                FROM KNIHY k
                LEFT JOIN AUTORY a ON a.ID = k.AUTOR_ID
                LEFT JOIN NAKLADATELSTVI n ON n.ID = k.NAKLADATELSTVI_ID
                LEFT JOIN KNIHY_DETAILY d ON d.KNIHYDET_ID = k.ID
                WHERE k.ID = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                using var r = cmd.ExecuteReader(CommandBehavior.SingleRow);
                if (r.Read())
                {
                    NameBooksBox.Text = r.IsDBNull(0) ? "" : r.GetString(0);
                    YearPrintBox.Text = r.IsDBNull(1) ? "" : r.GetInt32(1).ToString();
                    NumberOfPagesBox.Text = r.IsDBNull(2) ? "" : r.GetInt32(2).ToString();

                    var sr = r.IsDBNull(3) ? "" : r.GetString(3); // surname
                    var nm = r.IsDBNull(4) ? "" : r.GetString(4); // name
                    AuthorBox.Text = (sr + " " + nm).Trim();

                    var firm = r.IsDBNull(5) ? "" : r.GetString(5);
                    var city = r.IsDBNull(6) ? "" : r.GetString(6);
                    PublBox.Text = string.IsNullOrWhiteSpace(city) ? firm : $"{firm}, {city}";

                    DescBox.Text = r.IsDBNull(7) ? "" : r.GetString(7);
                }
            }

            using (var cmd = new FbCommand(@"
                SELECT z.NAZEV_ZANRY
                FROM ZANRY z
                JOIN KNIHY_ZANRY kg ON kg.ZANRY_ID = z.ZANRY_ID
                WHERE kg.KNIHY_ID = @id
                ORDER BY z.NAZEV_ZANRY", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                var names = new List<string>();
                using var r = cmd.ExecuteReader();
                while (r.Read()) names.Add(r.GetString(0));
                GenreBox.Text = string.Join(", ", names);
            }
        }

        // Load suggestions for popups (cities, genres)
        private void LoadSuggestions()
        {
            try
            {
                using var conn = new FbConnection(_connString);
                conn.Open();

                using (var cmd = new FbCommand(@"
                    SELECT DISTINCT TRIM(MESTO)
                    FROM NAKLADATELSTVI
                    WHERE MESTO IS NOT NULL
                    ORDER BY TRIM(MESTO)", conn))
                {
                    using var reader = cmd.ExecuteReader();
                    _allCities.Clear();
                    while (reader.Read())
                        _allCities.Add(reader.GetString(0));
                }

                using (var cmd = new FbCommand(@"
                    SELECT DISTINCT NAZEV_ZANRY
                    FROM ZANRY
                    WHERE NAZEV_ZANRY IS NOT NULL
                    ORDER BY NAZEV_ZANRY", conn))
                {
                    using var reader = cmd.ExecuteReader();
                    _allGenres.Clear();
                    while (reader.Read())
                        _allGenres.Add(reader.GetString(0));
                }
            }
            catch { /* ignore */ }
        }

        // Block paste if it contains non-digit characters
        private void NumericOnly_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            var tb = (TextBox)sender;
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = e.DataObject.GetData(DataFormats.Text) as string;
                if (string.IsNullOrEmpty(text) || !text.All(char.IsDigit))
                {
                    e.CancelCommand();
                    ShowNumErrorFor(tb, "Lze zadat pouze číslice.");
                }
            }
            else
            {
                e.CancelCommand();
                ShowNumErrorFor(tb, "Lze zadat pouze číslice.");
            }
        }

        // Block Space key (safety)
        private void NumericOnly_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                ShowNumErrorFor((TextBox)sender, "Lze zadat pouze číslice.");
            }
        }

        // Allow only 0–9 on typing
        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            bool ok = e.Text.All(char.IsDigit);
            if (!ok)
            {
                e.Handled = true; // prevent character from being entered
                ShowNumErrorFor(tb, "Lze zadat pouze číslice.");
            }
            else
            {
                HideNumErrorFor(tb); // hide error when input becomes valid
            }
        }

        // Pick cover file (UI text stays in Czech)
        private void PickCover_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Vyberte obálku knihy",
                Filter = "Obrázky (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|Všechny soubory|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                _selectedFilePath = dlg.FileName;
                CoverFileLabel.Text = Path.GetFileName(_selectedFilePath);
            }
        }

        private void PublBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!CityPopup.IsOpen) return;

            if (e.Key == Key.Down)
            {
                _cityIndex = Math.Min(_cityIndex + 1, CityList.Items.Count - 1);
                CityList.SelectedIndex = _cityIndex;
                CityList.ScrollIntoView(CityList.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                _cityIndex = Math.Max(_cityIndex - 1, 0);
                CityList.SelectedIndex = _cityIndex;
                CityList.ScrollIntoView(CityList.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && CityList.SelectedItem is string city)
            {
                ApplyCitySuggestions(city);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CityPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        // Type-ahead for publisher city (after the comma)
        private void PublBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = PublBox.Text ?? "";
            int sep = text.LastIndexOf(',');
            if (sep < 0) { CityPopup.IsOpen = false; return; }

            string after = text[(sep + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(after))
            {
                CityPopup.IsOpen = false;
                return;
            }

            var items = _allCities
                .Where(c => c.StartsWith(after, StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .ToList();

            CityList.ItemsSource = items;

            if (items.Count > 0)
            {
                PlacePopupUnderCaret(CityPopup, PublBox);
                CityPopup.IsOpen = true;
                _cityIndex = -1;
            }
            else
            {
                CityPopup.IsOpen = false;
            }
        }

        // Remove red border when content changes
        private void RemoveErrorOnChange(object? sender, TextChangedEventArgs e)
        {
            if (sender == YearPrintBox)
            {
                if (IsDigitsOrEmpty(YearPrintBox.Text)) HideNumErrorFor(YearPrintBox);
                ClearError(YearBorder, YearPrintBox);
            }
            else if (sender == NumberOfPagesBox)
            {
                if (IsDigitsOrEmpty(NumberOfPagesBox.Text)) HideNumErrorFor(NumberOfPagesBox);
                ClearError(StranBorder, NumberOfPagesBox);
            }
            else if (sender == NameBooksBox) ClearError(NameBorder, NameBooksBox);
            else if (sender == AuthorBox) ClearError(AuthorBorder, AuthorBox);
        }

        // Save/update the book
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ClearError(NameBorder, NameBooksBox);
            ClearError(AuthorBorder, AuthorBox);
            ClearError(YearBorder, YearPrintBox);
            ClearError(StranBorder, NumberOfPagesBox);

            var name = (NameBooksBox.Text ?? "").Trim();
            var authorFull = (AuthorBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Název knihy je povinné pole.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                MarkError(NameBorder, NameBooksBox);
                NameBooksBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(authorFull))
            {
                MessageBox.Show("Autor knihy je povinné pole.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                MarkError(AuthorBorder, AuthorBox);
                AuthorBox.Focus();
                return;
            }

            int? year = int.TryParse(YearPrintBox.Text, out var r) ? r : (int?)null;
            int? pages = int.TryParse(NumberOfPagesBox.Text, out var p) ? p : (int?)null;

            SplitAuthor(authorFull, out var surname, out var nameAuthor);
            ParsePubl(PublBox.Text, out var publName, out var publCity);

            try
            {
                await Manager.SaveOrUpdateBookAsync(
                    _connString,
                    _knihyID,
                    name,
                    surname, nameAuthor,
                    publName, publCity,
                    year, pages,
                    DescBox.Text,
                    _selectedFilePath,
                    StoreCoverFile,
                    ToAbsolutePath,
                    GenreBox.Text);

                DialogResult = true; // tell the main window to refresh
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nepodarilo se ulozit knihu: " + ex.Message,
                                "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Show numeric error popups immediately (typing space, non-digit, paste)
        private void ShowNumErrorFor(TextBox tb, string message)
        {
            if (tb == YearPrintBox)
            {
                RokErrText.Text = message;
                RokErrPopup.IsOpen = true;
            }
            else if (tb == NumberOfPagesBox)
            {
                StranErrText.Text = message;
                StranErrPopup.IsOpen = true;
            }
        }
    }
}