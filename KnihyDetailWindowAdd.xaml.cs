using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.IO;
using System.Configuration;
using FirebirdSql.Data.FirebirdClient;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Controls.Primitives;

namespace ukol1
{
    public partial class KnihyDetailWindowAdd : Window
    {
        private readonly string _connString =
                      ConfigurationManager
            .ConnectionStrings["KNIHOVNA2"]
            .ConnectionString;

        private readonly int? _knihyID;
        private List<string> _allCities = new();
        private List<string> _allGenres = new();
        private int _cityIndex = -1;
        private int _genresIndex = -1;
        private string? _selectedFilePath;

        //okno pro pridani nebo upravu knihy
        public KnihyDetailWindowAdd(int? knihyID = null)
        {
            InitializeComponent();
            DataContext = this;

            _knihyID = knihyID;
            this.Title = _knihyID.HasValue ? "Upravit knihu" : "Pridani nove knihy";

            if (_knihyID.HasValue)
                LoadFields(_knihyID.Value);

            LoadSuggestions();

            NakladatBox.TextChanged += NakladatBox_TextChanged;
            NakladatBox.PreviewKeyDown += NakladatBox_PreviewKeyDown;

            ZanryBox.TextChanged += ZanryBox_TextChanged;
            ZanryBox.PreviewKeyDown += ZanryBox_PreviewKeyDown;

            if (_knihyID.HasValue)
            {
                LoadFields(_knihyID.Value);
            }
        }

        //pro ukladani obalu knih
        private static string CoversRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "covers");

        //ulozi soubor do covers a vrati relativni cestu
        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "cover" : cleaned;
        }

        //rozdeli na nazev a mesto - nakladatelstvi
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

        //rozdeli autora na prijmeni a jmeno
        private static void SplitAuthor(string full, out string prijmeni, out string jmeno)
        {
            prijmeni = ""; jmeno = "";
            if (string.IsNullOrWhiteSpace(full)) return;
            var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) { prijmeni = parts[0]; return; }
            prijmeni = parts[^1];
            jmeno = string.Join(" ", parts.Take(parts.Length - 1));
        }

        private static string StoreCoverFile(string sourcePath, int bookId)
        {
            Directory.CreateDirectory(CoversRoot);

            var original = Path.GetFileName(sourcePath);
            var safe = MakeSafeFileName(original);

            var baseName = Path.GetFileNameWithoutExtension(safe);
            var ext = Path.GetExtension(safe);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            ext = ext.ToLowerInvariant();

            //unique name
            string targetName = $"{baseName}{bookId}{ext}";
            string targetRel = Path.Combine("covers", targetName).Replace('\\', '/');
            string targetAbs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, targetRel);

            //if exist = pridat cislovani
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

            //covers/...
            return targetRel;
        }

        //databazova = absolutni
        private static string? ToAbsolutePath(string? dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath)) return null;
            return Path.IsPathRooted(dbPath) ? dbPath :
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
        }

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

            ZanryBox.CaretIndex = ZanryBox.Text.Length;
            GenrePopup.IsOpen = false;
        }

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

        //dalsi volne ID pro knihy
        private int GetNextBookID(FbConnection conn, FbTransaction tx)
        {
            using var cmd = new FbCommand("SELECT COALESCE(MAX(ID), 0) + 1 FROM KNIHY;", conn, tx);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        //vrati volne ID pro konkretni tabilku a sloupec
        private int GetNextID(FbConnection conn, FbTransaction tx, string tableName, string idColumn)
        {
            using var cmd = new FbCommand($"SELECT COALESCE(MAX({idColumn}), 0) + 1 FROM {tableName};", conn, tx);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        //najit autora, pokud ne = vytvorit a vratit jeho ID
        private int GetOrCreateAuthor(FbConnection conn, FbTransaction tx, string full)
        {
            SplitAuthor(full, out var jmeno, out var prijmeni);

            using (var cmd = new FbCommand(
                "SELECT ID FROM AUTORY WHERE PRIJMENI=@pr AND JMENO=@jm", conn, tx))
            {
                cmd.Parameters.AddWithValue("pr", prijmeni);
                cmd.Parameters.AddWithValue("jm", jmeno);
                var o = cmd.ExecuteScalar();
                if (o != null && o != DBNull.Value) return Convert.ToInt32(o);
            }

            int newId = GetNextID(conn, tx, "AUTORY", "ID");
            using (var ins = new FbCommand(
                "INSERT INTO AUTORY (ID, PRIJMENI, JMENO) VALUES (@id, @pr, @jm)", conn, tx))
            {
                ins.Parameters.AddWithValue("id", newId);
                ins.Parameters.AddWithValue("pr", prijmeni ?? "");
                ins.Parameters.AddWithValue("jm", jmeno ?? "");
                ins.ExecuteNonQuery();
            }
            return newId;
        }

        //najde nebo vytvori zanr a vrati jeho ID
        private int GetOrCreateGenre(FbConnection conn, FbTransaction tx, string name)
        {
            using (var cmd = new FbCommand("SELECT GENRE_ID FROM GENRES WHERE NAME_GENRE=@n", conn, tx))
            {
                cmd.Parameters.AddWithValue("n", name);
                var o = cmd.ExecuteScalar();
                if (o != null && o != DBNull.Value) return Convert.ToInt32(o);
            }

            int newId = GetNextID(conn, tx, "GENRES", "GENRE_ID");
            using (var ins = new FbCommand(
                "INSERT INTO GENRES (GENRE_ID, NAME_GENRE) VALUES (@id, @n)", conn, tx))
            {
                ins.Parameters.AddWithValue("id", newId);
                ins.Parameters.AddWithValue("n", name ?? "");
                ins.ExecuteNonQuery();
            }
            return newId;
        }

        //nakladatelstvi - najde nebo vytvori a vrati jeho ID
        private int GetOrCreateNaklad(FbConnection conn, FbTransaction tx, string input)
        {
            ParselNaklad(input, out var name, out var mesto);
            using (var cmd = new FbCommand(
                "SELECT ID FROM NAKLADATELSTVI WHERE UPPER(NAZEV_FIRMY) = UPPER(@n) " +
                "AND UPPER(COALESCE(MESTO,'')) = UPPER(@m)", conn, tx))
            {
                cmd.Parameters.AddWithValue("n", name ?? "");
                cmd.Parameters.AddWithValue("m", mesto ?? "");
                var o = cmd.ExecuteScalar();
                if (o != null && o != DBNull.Value) return Convert.ToInt32(o);
            }

            int newId = GetNextID(conn, tx, "NAKLADATELSTVI", "ID");
            using (var ins = new FbCommand(
                "INSERT INTO NAKLADATELSTVI (ID, NAZEV_FIRMY, MESTO) VALUES (@id, @n, @m)", conn, tx))
            {
                ins.Parameters.AddWithValue("id", newId);
                ins.Parameters.AddWithValue("n", name ?? "");
                ins.Parameters.AddWithValue("m", mesto ?? "");
                ins.ExecuteNonQuery();
            }
            return newId;
        }

        //nacteni data knih a vypleni podle folmularu
        private void LoadFields(int id)
        {
            using var conn = new FbConnection(_connString);
            conn.Open();

            using (var cmd = new FbCommand(@"SELECT
                                           k.NAZEV,
                                           k.ROK,
                                           k.POCET_STRAN,
                                           a.PRIJMENI,
                                           a.JMENO,
                                           n.NAZEV_FIRMY,
                                           d.POPIS_KNIHY
                                           FROM KNIHY k
                                           LEFT JOIN AUTORY a ON a.ID = k.AUTOR_ID
                                           LEFT JOIN NAKLADATELSTVI n ON n.ID = k.NAKLADATELSTVI_ID
                                           LEFT JOIN KNIHY_DETAILS d ON d.KNIHYDET_ID = k.ID
                                           WHERE k.ID=@id", conn))
            {
                cmd.Parameters.AddWithValue("ID", id);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    NazevKnihyBox.Text = r.IsDBNull(0) ? "" : r.GetString(0);
                    RokVydaniBox.Text = r.IsDBNull(1) ? "" : r.GetInt32(1).ToString();
                    PocetStranBox.Text = r.IsDBNull(2) ? "" : r.GetInt32(2).ToString();

                    var jm = r.IsDBNull(3) ? "" : r.GetString(3);
                    var pr = r.IsDBNull(4) ? "" : r.GetString(4);
                    AutorBox.Text = (pr + " " + jm).Trim();

                    NakladatBox.Text = r.IsDBNull(5) ? "" : r.GetString(5);
                    PopisBox.Text = r.IsDBNull(6) ? "" : r.GetString(6);
                }
            }

            using (var cmd = new FbCommand(@"
                    SELECT g.NAME_GENRE
                    FROM GENRES g
                    JOIN KNIHY_GENRES kg ON kg.GENRE_ID = g.GENRE_ID
                    WHERE kg.KNIHY_ID = @id
                    ORDER BY g.NAME_GENRE", conn))
            {
                cmd.Parameters.AddWithValue("ID", id);
                var names = new List<string>();
                using var r = cmd.ExecuteReader();
                while (r.Read()) names.Add(r.GetString(0));
                ZanryBox.Text = string.Join(", ", names);
            }
        }

        private void LoadSuggestions()
        {
            try
            {
                //nakladatelstvi mesto list
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
                    {
                        _allCities.Add(reader.GetString(0));
                    }
                }

                //zanry list
                using (var cmd = new FbCommand(@"
                    SELECT DISTINCT NAME_GENRE
                    FROM GENRES
                    ORDER BY NAME_GENRE", conn))
                {
                    using var reader = cmd.ExecuteReader();
                    _allGenres.Clear();
                    while (reader.Read())
                    {
                        _allGenres.Add(reader.GetString(0));
                    }
                }
            }
            catch { /*ignore*/ }
        }

        private void NakladatBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

        //ukazky pro nakladatelstvi
        private void NakladatBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = NakladatBox.Text ?? "";
            int sep = text.LastIndexOf(',');
            if (sep < 0)
            {
                CityPopup.IsOpen = false;
                return;
            }
            string after = text.Substring(sep + 1).Trim();
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

        //otevreni okna pro vyber obalky knihy
        private void PickCover_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Vyberte obal knihy",
                Filter = "Obrazky (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|Vsfechny soubory|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                _selectedFilePath = dlg.FileName;
                CoverFileLabel.Text = System.IO.Path.GetFileName(_selectedFilePath);
            }
        }

        //ulozeni/aktualizace podle vybranych udaju
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var nazev = (NazevKnihyBox.Text ?? "").Trim();
            var autor = (AutorBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nazev))
            {
                MessageBox.Show("Nazev knihy je povinne pole.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                NazevKnihyBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(autor))
            {
                MessageBox.Show("Autor knihy je povinne pole.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                AutorBox.Focus();
                return;
            }

            using var conn = new FbConnection(_connString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            int autorId = GetOrCreateAuthor(conn, tx, AutorBox.Text ?? string.Empty);
            int nakladId = GetOrCreateNaklad(conn, tx, NakladatBox.Text);

            int? rok = int.TryParse(RokVydaniBox.Text, out var r) ? r : (int?)null;
            int? stran = int.TryParse(PocetStranBox.Text, out var p) ? p : (int?)null;

            int bookId = _knihyID ?? GetNextBookID(conn, tx);

            if (_knihyID == null)
            {
                using var ins = new FbCommand(@"
                    INSERT INTO KNIHY (ID, NAZEV, ROK, AUTOR_ID, NAKLADATELSTVI_ID, POCET_STRAN)
                    VALUES (@id, @nazev, @rok, @aid, @nid, @ps)", conn, tx);

                ins.Parameters.AddWithValue("id", bookId);
                ins.Parameters.AddWithValue("nazev", NazevKnihyBox.Text);
                ins.Parameters.AddWithValue("rok", (object?)rok ?? DBNull.Value);
                ins.Parameters.AddWithValue("aid", autorId);
                ins.Parameters.AddWithValue("nid", nakladId);
                ins.Parameters.AddWithValue("ps", (object?)stran ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }
            else
            {
                using var upd = new FbCommand(@"
                                            UPDATE KNIHY SET
                                            NAZEV = @nazev,
                                            ROK = @rok,
                                            AUTOR_ID = @aid,
                                            NAKLADATELSTVI_ID = @nid,
                                            POCET_STRAN = @ps
                                            WHERE ID = @id", conn, tx);

                upd.Parameters.AddWithValue("id", bookId);
                upd.Parameters.AddWithValue("nazev", NazevKnihyBox.Text);
                upd.Parameters.AddWithValue("rok", (object?)rok ?? DBNull.Value);
                upd.Parameters.AddWithValue("aid", autorId);
                upd.Parameters.AddWithValue("nid", nakladId);
                upd.Parameters.AddWithValue("ps", (object?)stran ?? DBNull.Value);
                upd.ExecuteNonQuery();
            }

            string? oldRelPath = null;
            using (var cur = new FbCommand("SELECT KNIHY_PATH FROM KNIHY_DETAILS WHERE KNIHYDET_ID=@id", conn, tx))
            {
                cur.Parameters.AddWithValue("id", bookId);
                var curPath = cur.ExecuteScalar();
                if (curPath != null && curPath != DBNull.Value) oldRelPath = Convert.ToString(curPath);
            }

            string? newRelPath = oldRelPath;
            if (!string.IsNullOrWhiteSpace(_selectedFilePath))
            {
                newRelPath = StoreCoverFile(_selectedFilePath, bookId);

                if (!string.IsNullOrWhiteSpace(oldRelPath) && !string.Equals(oldRelPath, newRelPath, StringComparison.OrdinalIgnoreCase))
                {
                    var oldAbs = ToAbsolutePath(oldRelPath);
                    if (!string.IsNullOrWhiteSpace(oldAbs) && File.Exists(oldAbs))
                    {
                        try
                        {
                            File.Delete(oldAbs); //odstranim stary soubor pokud existuje
                        }
                        catch { /*ignore*/ }
                    }
                }
            }

            UpsertDetails(conn, tx, bookId, PopisBox.Text, newRelPath);
            SetGenres(conn, tx, bookId, ZanryBox.Text);

            tx.Commit();
            DialogResult = true;
            Close();
        }

        //nastavi zanry knihy v databazi
        private void SetGenres(FbConnection conn, FbTransaction tx, int bookId, string csv)
        {
            using (var del = new FbCommand(
                "DELETE FROM KNIHY_GENRES WHERE KNIHY_ID=@id", conn, tx))
            { del.Parameters.AddWithValue("id", bookId); del.ExecuteNonQuery(); }

            if (string.IsNullOrWhiteSpace(csv)) return;

            var names = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .Where(s => s.Length > 0)
                           .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
            {
                int gid = GetOrCreateGenre(conn, tx, name);
                using var ins = new FbCommand(
                    "INSERT INTO KNIHY_GENRES (KNIHY_ID, GENRE_ID) VALUES (@bid, @gid)", conn, tx);
                ins.Parameters.AddWithValue("bid", bookId);
                ins.Parameters.AddWithValue("gid", gid);
                ins.ExecuteNonQuery();
            }
        }

        //vlozeni/aktualizace detailu knihy
        private void UpsertDetails(FbConnection conn, FbTransaction tx, int bookId, string popis, string? relativePath)
        {
            using var upd = new FbCommand(
                "UPDATE KNIHY_DETAILS SET POPIS_KNIHY=@p, KNIHY_PATH=@path WHERE KNIHYDET_ID=@id", conn, tx);
            upd.Parameters.AddWithValue("p", (object?)popis ?? DBNull.Value);
            upd.Parameters.AddWithValue("path", (object?)relativePath ?? DBNull.Value);
            upd.Parameters.AddWithValue("id", bookId);
            if (upd.ExecuteNonQuery() == 0)
            {
                using var ins = new FbCommand(
                    "INSERT INTO KNIHY_DETAILS (KNIHYDET_ID, POPIS_KNIHY, KNIHY_PATH) VALUES (@id, @p, @path)", conn, tx);
                ins.Parameters.AddWithValue("id", bookId);
                ins.Parameters.AddWithValue("p", (object?)popis ?? DBNull.Value);
                ins.Parameters.AddWithValue("path", (object?)relativePath ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }
        }

        private void ZanryBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!GenrePopup.IsOpen) return;

            if (e.Key == Key.Down)
            {
                _genresIndex = Math.Max(_genresIndex - 1, 0);
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

        private void ZanryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = ZanryBox.Text ?? "";
            int sep = text.LastIndexOf(',');
            string token = (sep >= 0 ? text.Substring(sep + 1) : text).Trim();

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