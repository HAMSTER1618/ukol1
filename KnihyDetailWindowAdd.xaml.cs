using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FirebirdSql.Data.FirebirdClient;
using System.Threading.Tasks;

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

        //okno pro pridani/upravu knihy
        public KnihyDetailWindowAdd(int? knihyID = null)
        {
            InitializeComponent();
            DataContext = this;

            _knihyID = knihyID;
            //UI text s diakritikou
            this.Title = _knihyID.HasValue ? "Upravit knihu" : "Přidání nové knihy";

            if (_knihyID.HasValue)
                LoadFields(_knihyID.Value);

            LoadSuggestions();

            //hooky na naseptavace
            NakladatBox.TextChanged += NakladatBox_TextChanged;
            NakladatBox.PreviewKeyDown += NakladatBox_PreviewKeyDown;

            ZanryBox.TextChanged += ZanryBox_TextChanged;
            ZanryBox.PreviewKeyDown += ZanryBox_PreviewKeyDown;

            //cisla pouze
            HookNumericOnly(RokVydaniBox);
            HookNumericOnly(PocetStranBox);

            //zruseni chyboveho oznaceni pri zmene
            NazevKnihyBox.TextChanged += RemoveErrorOnChange;
            AuthorBox.TextChanged += RemoveErrorOnChange;
        }

        //kořen slozky pro obalky
        private static string CoversRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "covers");

        //UI helper: smazat oznaceni chyby
        private static void ClearError(Border border, TextBox tb)
        {
            border.ClearValue(BorderBrushProperty);
            border.ClearValue(BorderThicknessProperty);
            tb.ClearValue(BackgroundProperty);
        }

        // kontrola: povolene jsou pouze cislice nebo prazdny retezec
        private static bool IsDigitsOrEmpty(string? s) => string.IsNullOrWhiteSpace(s) || s!.All(char.IsDigit);

        //vytvorit bezpecne jmeno souboru
        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "cover" : cleaned;
        }

        //UI helper: oznacit chybu
        private static void MarkError(Border border, TextBox tb)
        {
            border.BorderBrush = Brushes.Red;
            border.BorderThickness = new Thickness(1.5);
            tb.Background = new SolidColorBrush(Color.FromRgb(255, 240, 240));
        }

        //rozdeleni nakladatelstvi ve formatu "firma, mesto"
        private static void ParselNaklad(string input, out string nazev, out string mesto)
        {
            nazev = ""; mesto = "";
            if (string.IsNullOrWhiteSpace(input)) return;
            var parts = input.Split(new[] { ',' }, 2, StringSplitOptions.RemoveEmptyEntries)
                             .Select(part => part.Trim())
                             .ToArray();
            if (parts.Length > 0) nazev = parts[0];
            if (parts.Length > 1) mesto = parts[1];
        }

        //umistit popup pod kurzor
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

        //rozdelit autora na prijmeni a jmeno
        private static void SplitAuthor(string full, out string prijmeni, out string jmeno)
        {
            prijmeni = ""; jmeno = "";
            if (string.IsNullOrWhiteSpace(full)) return;
            var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) { prijmeni = parts[0]; return; }
            prijmeni = parts[^1];
            jmeno = string.Join(" ", parts.Take(parts.Length - 1));
        }

        //ulozit obalku a vratit relativni cestu
        private static string StoreCoverFile(string sourcePath, int bookId)
        {
            Directory.CreateDirectory(CoversRoot);

            var original = Path.GetFileName(sourcePath);
            var safe = MakeSafeFileName(original);

            var baseName = Path.GetFileNameWithoutExtension(safe);
            var ext = Path.GetExtension(safe);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            ext = ext.ToLowerInvariant();

            //unikatni jmeno
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

        //prevod relativni cesty ulozene v DB na absolutni
        private static string? ToAbsolutePath(string? dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath)) return null;
            return Path.IsPathRooted(dbPath)
                ? dbPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
        }

        //aplikace vybraneho mesta do textboxu
        private void ApplyCitySuggestions(string city)
        {
            var text = NakladatBox.Text ?? "";
            int sep = text.LastIndexOf(',');

            if (sep >= 0)
            {
                var head = text.Substring(0, sep).TrimEnd(' ', '\t', ',');
                NakladatBox.Text = string.IsNullOrWhiteSpace(head) ? city : $"{head}, {city}";
            }
            else
            {
                NakladatBox.Text = city;
            }

            NakladatBox.CaretIndex = NakladatBox.Text.Length;
            CityPopup.IsOpen = false;
        }

        //aplikace vybraneho zanru do textboxu
        private void ApplyGenreSuggestions(string genre)
        {
            var text = ZanryBox.Text ?? "";
            int sep = text.LastIndexOf(',');
            if (sep >= 0)
            {
                var head = text.Substring(0, sep).TrimEnd(' ', '\t', ',');
                ZanryBox.Text = string.IsNullOrWhiteSpace(head) ? genre : $"{head}, {genre}";
            }
            else
            {
                ZanryBox.Text = genre;
            }

            // nastav kurzor na konec a fokus zpet do textboxu
            ZanryBox.CaretIndex = ZanryBox.Text.Length;   // kurzor na konec
            ZanryBox.Focus();                              // fokus na textbox
            Keyboard.Focus(ZanryBox);
            GenrePopup.IsOpen = false;
        }

        //zrusit a zavrit
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

        private void GenreList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (GenreList.SelectedItem is string genre)
                ApplyGenreSuggestions(genre);
        }

        // skryje popup chyby pro dany TextBox
        private void HideNumErrorFor(TextBox tb)
        {
            if (tb == RokVydaniBox) RokErrPopup.IsOpen = false;
            else if (tb == PocetStranBox) StranErrPopup.IsOpen = false;
        }

        //povolit pouze cislice (vkladani + psani)
        private void HookNumericOnly(TextBox tb)
        {
            // povoli jen cislice pri psani
            tb.PreviewTextInput += NumericOnly_PreviewTextInput;
            // zakaze mezeru
            tb.PreviewKeyDown += NumericOnly_PreviewKeyDown;
            // kontrola pri vlozeni (Ctrl+V)
            DataObject.AddPastingHandler(tb, NumericOnly_OnPaste);
            // pri kazde zmene textu skryje chybu, kdyz je vstup validni
            tb.TextChanged += RemoveErrorOnChange;
        }

        //nacteni hodnot do formulare
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
                    NazevKnihyBox.Text = r.IsDBNull(0) ? "" : r.GetString(0);
                    RokVydaniBox.Text = r.IsDBNull(1) ? "" : r.GetInt32(1).ToString();
                    PocetStranBox.Text = r.IsDBNull(2) ? "" : r.GetInt32(2).ToString();

                    var pr = r.IsDBNull(3) ? "" : r.GetString(3); //prijmeni
                    var jm = r.IsDBNull(4) ? "" : r.GetString(4); //jmeno
                    AuthorBox.Text = (pr + " " + jm).Trim();

                    var firma = r.IsDBNull(5) ? "" : r.GetString(5);
                    var mesto = r.IsDBNull(6) ? "" : r.GetString(6);
                    NakladatBox.Text = string.IsNullOrWhiteSpace(mesto) ? firma : $"{firma}, {mesto}";

                    PopisBox.Text = r.IsDBNull(7) ? "" : r.GetString(7);
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
                ZanryBox.Text = string.Join(", ", names);
            }
        }

        //nacteni seznamu pro naseptavace (mesta, zanry)
        private void LoadSuggestions()
        {
            try
            {
                using var conn = new FbConnection(_connString);
                conn.Open();

                using (var cmd = new FbCommand(@"
                    SELECT DISTINCT TRIM(MESTO)
                    FROM NAKLADATELSTVI WHERE MESTO IS NOT NULL
                    ORDER BY TRIM(MESTO)", conn))
                {
                    using var reader = cmd.ExecuteReader();
                    _allCities.Clear();
                    while (reader.Read())
                        _allCities.Add(reader.GetString(0));
                }

                using (var cmd = new FbCommand(@"
                    SELECT DISTINCT NAZEV_ZANRY
                    FROM ZANRY WHERE NAZEV_ZANRY IS NOT NULL
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

        private void NakladatBox_PreviewKeyDown(object sender, KeyEventArgs e)
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

        //naseptavac pro mesta (nakladatelstvi)
        private void NakladatBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = NakladatBox.Text ?? "";
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
                PlacePopupUnderCaret(CityPopup, NakladatBox);
                CityPopup.IsOpen = true;
                _cityIndex = -1;
            }
            else
            {
                CityPopup.IsOpen = false;
            }
        }

        // blokovat paste s neciselnyma znaky
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

        // blokovat mezernik (pro jistotu i kdyby TextInput proslo)
        private void NumericOnly_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                ShowNumErrorFor((TextBox)sender, "Lze zadat pouze číslice.");
            }
        }

        //povolit jen cisla 0-9
        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            bool ok = e.Text.All(char.IsDigit);
            if (!ok)
            {
                e.Handled = true;                            // zamezi vlozeni znaku
                ShowNumErrorFor(tb, "Lze zadat pouze číslice.");
            }
            else
            {
                HideNumErrorFor(tb);                         // pri spravnem vstupu skryj chybu
            }
        }

        //otevrit dialog a vybrat obalku (UI s diakritikou)
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

        //pri zmene textu odstranit cervene oramovani
        private void RemoveErrorOnChange(object? sender, TextChangedEventArgs e)
        {
            if (sender == RokVydaniBox)
            {
                if (IsDigitsOrEmpty(RokVydaniBox.Text)) HideNumErrorFor(RokVydaniBox);
                ClearError(RokBorder, RokVydaniBox);
            }
            else if (sender == PocetStranBox)
            {
                if (IsDigitsOrEmpty(PocetStranBox.Text)) HideNumErrorFor(PocetStranBox);
                ClearError(StranBorder, PocetStranBox);
            }
            else if (sender == NazevKnihyBox) ClearError(NazevBorder, NazevKnihyBox);
            else if (sender == AuthorBox) ClearError(AuthorBorder, AuthorBox);
        }

        //ulozit/aktualizovat knihu
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ClearError(NazevBorder, NazevKnihyBox);
            ClearError(AuthorBorder, AuthorBox);
            ClearError(RokBorder, RokVydaniBox);
            ClearError(StranBorder, PocetStranBox);

            var nazev = (NazevKnihyBox.Text ?? "").Trim();
            var Author = (AuthorBox.Text ?? "").Trim();
            var authorFull = (AuthorBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nazev))
            {
                MessageBox.Show("Název knihy je povinné pole.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                MarkError(NazevBorder, NazevKnihyBox);
                NazevKnihyBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(Author))
            {
                MessageBox.Show("Autor knihy je povinné pole.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                MarkError(AuthorBorder, AuthorBox);
                AuthorBox.Focus();
                return;
            }

            int? rok = int.TryParse(RokVydaniBox.Text, out var r) ? r : (int?)null;
            int? stran = int.TryParse(PocetStranBox.Text, out var p) ? p : (int?)null;

            SplitAuthor(authorFull, out var jmeno, out var prijmeni);
            ParselNaklad(NakladatBox.Text, out var nakladNazev, out var nakladMesto);

            try
            {
                // Ulozit do DB (async obalka nad existujicim kodem v MvvmBD)
                await MvvmBD.SaveOrUpdateBookAsync(
                    _connString,
                    _knihyID,
                    nazev,
                    jmeno, prijmeni,
                    nakladNazev, nakladMesto,
                    rok, stran,
                    PopisBox.Text,
                    _selectedFilePath,
                    StoreCoverFile,
                    ToAbsolutePath,
                    ZanryBox.Text
                );

                DialogResult = true; // hlavni okno pozna, ze ma refreshnout tabulky
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nepodarilo se ulozit knihu: " + ex.Message,
                                "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ukaz chybu okamzite pri zakazanych vstupech (mezera, necislo, paste)
        private void ShowNumErrorFor(TextBox tb, string message)
        {
            if (tb == RokVydaniBox)
            {
                RokErrText.Text = message;
                RokErrPopup.IsOpen = true;
            }
            else if (tb == PocetStranBox)
            {
                StranErrText.Text = message;
                StranErrPopup.IsOpen = true;
            }
        }

        private void ZanryBox_PreviewKeyDown(object sender, KeyEventArgs e)
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

        //naseptavac pro zanry
        private void ZanryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = ZanryBox.Text ?? "";
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
                PlacePopupUnderCaret(GenrePopup, ZanryBox);
                GenrePopup.IsOpen = true;
                _genresIndex = -1;
            }
            else
            {
                GenrePopup.IsOpen = false;
            }
        }
    }
}