using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;   // Hyperlink
using System.Windows.Input;
using FirebirdSql.Data.FirebirdClient;
using ukol1;
using static ukol1.MvvmBD;

namespace ukol1
{
    public partial class MainWindow : Window
    {
        private readonly string _connString =
            ConfigurationManager.ConnectionStrings["KNIHOVNA2"].ConnectionString;

        private bool _isReloading;
        private bool _openingDetails;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += async (s, e) => { CleanDBonStart(); await ReloadAllTableAsync(); };

            //kontextové menu
            CtxEdit.Click += async (s, e) => await EditSelectedAsync();
            CtxDelete.Click += async (s, e) => await DeleteSelectedAsync();
        }

        //Univerzal pro SELECT -> DataView (bezi mimo UI vlakno, aby se okno nezamykalo)
        private static Task<DataView> LoadDataViewAsync(string connStr, string sql)
        {
            return Task.Run(() =>
            {
                using var conn = new FbConnection(connStr);
                using var cmd = new FbCommand(sql, conn);
                using var da = new FbDataAdapter(cmd);
                var dt = new DataTable();
                conn.Open();
                da.Fill(dt);
                return dt.DefaultView;
            });
        }

        //kontrola tabulky na začátku – smazat osamělé řádky bez knih
        private void CleanDBonStart()
        {
            using var conn = new FbConnection(_connString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            //smazání autorů bez knih
            using (var cmd = new FbCommand(@"
                DELETE FROM AUTORY
                WHERE NOT EXISTS (
                    SELECT 1 FROM KNIHY WHERE KNIHY.AUTOR_ID = AUTORY.ID
                )", conn, tx))
            {
                cmd.ExecuteNonQuery();
            }

            //smazání nakladatelství bez knih
            using (var cmd = new FbCommand(@"
                DELETE FROM NAKLADATELSTVI
                WHERE NOT EXISTS (
                    SELECT 1 FROM KNIHY WHERE KNIHY.NAKLADATELSTVI_ID = NAKLADATELSTVI.ID
                )", conn, tx))
            {
                cmd.ExecuteNonQuery();
            }

            //smazání žánrů bez knih
            using (var cmd = new FbCommand(@"
                DELETE FROM ZANRY
                WHERE NOT EXISTS (
                    SELECT 1 FROM KNIHY_ZANRY kg WHERE kg.ZANRY_ID = ZANRY.ZANRY_ID
                )", conn, tx))
            {
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        //buttony jsou schované, ale ponechány pro případ návratu na tlačítka
        private async void CtxDelete_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

        private void CtxEdit_Click(object sender, RoutedEventArgs e) => EditSelectedAsync();

        //logika pro smazání přes button (teď schováno) – jen přesměrování
        private void DeleteBook_Click(object sender, RoutedEventArgs e) => DeleteSelectedAsync();

        //smazání knihy (pravé tlačítko / Delete)
        //Smazat vybranou knihu (potvrzeni) a po smazani obnovit tabulky
        private async Task DeleteSelectedAsync()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            if (TableBooks.SelectedItem is DataRowView row)
            {
                string? nazev = row["NAZEV"] as string;
                if (MessageBox.Show(
                    $"Opravdu chcete smazat knihu: \"{nazev}\"?",
                    "Potvrzeni", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            MvvmBD.DeleteBookCascadeAsync(_connString, id.Value); //synch. DB mazani
            await ReloadAllTableAsync();                     //refresh
        }

        //oprava knihy + aktualizace tabulek (schovaný button ponechán)
        private async void EditBook_Click(object sender, RoutedEventArgs e) => EditSelectedAsync();

        //Otevrit dialog pro upravu vybrane knihy a pak obnovit tabulky
        private async Task EditSelectedAsync()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            var win = new KnihyDetailWindowAdd(id.Value) { Owner = this };
            if (win.ShowDialog() == true)
                await ReloadAllTableAsync();
        }

        //načítání ID vybrané knihy (funguje i pro BookRow, i pro DataRowView)
        private int? GetSelectedBookID()
        {
            var item = TableBooks.SelectedItem;
            if (item == null) return null;

            // коли ItemsSource = DataTable / DefaultView
            if (item is DataRowView drv)
                return Convert.ToInt32(drv["ID"]);

            // будь-який об'єкт, що має властивість ID
            var prop = item.GetType().GetProperty("ID");
            if (prop != null)
            {
                var val = prop.GetValue(item);
                if (val != null) return Convert.ToInt32(val);
            }
            return null;
        }

        private async void OpenBookLink_Click(object sender, RoutedEventArgs e)
        {
            int id;
            if (sender is Hyperlink h)
            {
                if (h.DataContext is BookRow br) id = br.ID;
                else if (h.DataContext is DataRowView drv) id = Convert.ToInt32(drv["ID"]);
                else return;
            }
            else return;

            // Otevrit detail a po zavreni znovu nacist tabulky
            var win = new KnihyDetailWindow(id) { Owner = this };
            win.ShowDialog();
            await ReloadAllTableAsync();
        }

        private async Task OpenDetailsForSelected()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            if (_openingDetails) return;
            _openingDetails = true;
            try
            {
                var win = new KnihyDetailWindow(id.Value) { Owner = this };
                win.ShowDialog();
                await ReloadAllTableAsync();
            }
            finally
            {
                _openingDetails = false;
            }
        }

        private async void PridatKnihu_Click(object sender, RoutedEventArgs e)
        {
            var win = new KnihyDetailWindowAdd { Owner = this };
            if (win.ShowDialog() == true)
                await ReloadAllTableAsync();
        }

        //aktualizace všech tabulek a jejich dat
        //nacteni vsech tabulek (async)
        private async Task ReloadAllTableAsync()
        {
            _isReloading = true;
            try
            {
                //Knihy – bereme jako List<BookRow>, pak vynutime prekresleni gridu
                var books = await MvvmBD.GetBooksAsync(_connString);
                TableBooks.ItemsSource = null;        //vynuti prekresleni
                TableBooks.ItemsSource = books;

                //Autori a Nakladatelstvi – nahrat jako DataView na pozadi
                TablAutori.ItemsSource = await LoadDataViewAsync(_connString, "SELECT * FROM AUTORY ORDER BY ID");
                TablNakladatelstvi.ItemsSource = await LoadDataViewAsync(_connString, "SELECT * FROM NAKLADATELSTVI ORDER BY ID");

                TableBooks.SelectedItem = null;
                BtnPridatKnihu.IsEnabled = true;
            }
            finally
            {
                _isReloading = false;
            }
        }

        private async void TableBooks_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await EditSelectedAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                await DeleteSelectedAsync();
                e.Handled = true;
            }
        }

        private void TableBooks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isReloading && !_openingDetails)
            {
                //bool hasSel = TableBooks.SelectedItem is DataRowView;
                //BtnUpravitKnihu.IsEnabled = hasSel;
                //BtnSmazatKnihu.IsEnabled = hasSel;
            }
        }

        //levy klik - otevření detailů (kursor na řádku ruka)
        private void TablKnihy_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isReloading || _openingDetails) return;
            OpenDetailsForSelected();
        }
    }
}