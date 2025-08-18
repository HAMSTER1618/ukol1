using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;   //Hyperlink
using System.Windows.Input;
using FirebirdSql.Data.FirebirdClient;

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

            Loaded += (s, e) =>
            {
                CleanDBonStart();
                ReloadAllTable();
            };

            //kontextové menu
            CtxEdit.Click += (s, e) => EditSelected();
            CtxDelete.Click += (s, e) => DeleteSelected();
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
        private void CtxDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        private void CtxEdit_Click(object sender, RoutedEventArgs e) => EditSelected();

        //logika pro smazání přes button (teď schováno) – jen přesměrování
        private void DeleteBook_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        //smazání knihy (pravé tlačítko / Delete)
        private void DeleteSelected()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            //potvrzení
            string? nazev = null;
            var sel = TableBooks.SelectedItem;
            if (sel is BookRow br) nazev = br.Nazev;
            else if (sel is DataRowView drv) nazev = drv["NAZEV"] as string;

            if (MessageBox.Show(
                    $"Opravdu chcete smazat knihu: \"{nazev}\"?",
                    "Potvrzení", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            //smazání + refresh
            MvvmBD.DeleteBookCascade(_connString, id.Value);
            ReloadAllTable();
        }

        //oprava knihy + aktualizace tabulek (schovaný button ponechán)
        private void EditBook_Click(object sender, RoutedEventArgs e) => EditSelected();

        //dostávání ID vybrané knihy a otevření detailního okna pro editaci
        private void EditSelected()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            var win = new KnihyDetailWindowAdd(id.Value) { Owner = this };
            if (win.ShowDialog() == true)
                ReloadAllTable();
        }

        //načítání ID vybrané knihy (funguje i pro BookRow, i pro DataRowView)
        private int? GetSelectedBookID()
        {
            var item = TableBooks.SelectedItem;
            if (item == null) return null;

            if (item is BookRow br) return br.ID;
            if (item is DataRowView drv) return Convert.ToInt32(drv["ID"]);

            var prop = item.GetType().GetProperty("ID");
            return prop != null ? Convert.ToInt32(prop.GetValue(item)) : (int?)null;
        }

        //načítání dat do libovolné tabulky (používá se pro Autory/Nakladatelství)
        private void LoadTable(string sql, DataGrid grid)
        {
            var dt = new DataTable();
            try
            {
                using var conn = new FbConnection(_connString);
                using var cmd = new FbCommand(sql, conn);
                using var da = new FbDataAdapter(cmd);

                conn.Open();
                da.Fill(dt);
                grid.ItemsSource = dt.DefaultView;
            }
            catch
            {
                //ignore
            }
        }

        private void OpenBookLink_Click(object sender, RoutedEventArgs e)
        {
            int id;
            if (sender is Hyperlink h)
            {
                if (h.DataContext is BookRow br)
                    id = br.ID;
                else if (h.DataContext is DataRowView drv)
                    id = Convert.ToInt32(drv["ID"]);
                else
                    return;
            }
            else return;

            //otevření detailního okna pro vybranou knihu
            var win = new KnihyDetailWindow(id) { Owner = this };
            win.ShowDialog();
            ReloadAllTable();
        }

        private void OpenDetailsForSelected()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            if (_openingDetails) return;
            _openingDetails = true;
            try
            {
                var win = new KnihyDetailWindow(id.Value) { Owner = this };
                win.ShowDialog();
                ReloadAllTable();
            }
            finally
            {
                _openingDetails = false;
            }
        }

        private void PridatKnihu_Click(object sender, RoutedEventArgs e)
        {
            var win = new KnihyDetailWindowAdd { Owner = this };
            if (win.ShowDialog() == true)
                ReloadAllTable();
        }

        //aktualizace všech tabulek a jejich dat
        private void ReloadAllTable()
        {
            _isReloading = true;
            try
            {
                //knihy načítáme přes MvvmBD (List<BookRow>)
                TableBooks.ItemsSource = MvvmBD.GetBooks(_connString);

                //zbylé tabulky – přímo SQL
                LoadTable("SELECT * FROM AUTORY ORDER BY ID", TablAutori);
                LoadTable("SELECT * FROM NAKLADATELSTVI ORDER BY ID", TablNakladatelstvi);

                TableBooks.SelectedItem = null;
                BtnPridatKnihu.IsEnabled = true;
            }
            finally
            {
                _isReloading = false;
            }
        }

        private void TableBooks_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                EditSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelected();
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