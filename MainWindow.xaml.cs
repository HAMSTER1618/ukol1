using System;
using System.Data;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using FirebirdSql.Data.FirebirdClient;
using System.IO;

namespace ukol1
{
    public partial class MainWindow : Window
    {
        private readonly string _connString =
            ConfigurationManager.ConnectionStrings["KNIHOVNA2"].ConnectionString;

        private bool _isReloading;
        private bool _openingDetails;
        //private bool _isLoadingDetails;
        //

        public MainWindow()
        {
            InitializeComponent();
            //Loaded += MainWindow_Loaded;
            Loaded += (s, e) => { CleanDBonStart(); ReloadAllTable(); };
            CtxEdit.Click += (s, e) => EditSelected();
            CtxDelete.Click += (s, e) => DeleteSelected();
        }

        private void AddBook_Click(object sender, RoutedEventArgs e)
        {
            var win = new KnihyDetailWindowAdd { Owner = this };
            if (win.ShowDialog() == true)
            {
                ReloadAllTable();
            }
        }

        private void CleanDBonStart()
        {
            using var conn = new FbConnection(_connString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            //smazat autore bez knih
            using (var cmd = new FbCommand(@"DELETE FROM AUTORY WHERE NOT EXISTS (SELECT 1 FROM KNIHY
                                                WHERE KNIHY.AUTOR_ID = AUTORY.ID)", conn, tx))
            {
                cmd.ExecuteNonQuery();
            }
            //smazani nakladatelstvi bez knih
            using (var cmd = new FbCommand(@"DELETE FROM NAKLADATELSTVI WHERE NOT EXISTS (SELECT 1 FROM KNIHY
                                                WHERE KNIHY.NAKLADATELSTVI_ID = NAKLADATELSTVI.ID)", conn, tx))
            {
                cmd.ExecuteNonQuery();
            }
            //smazani zanry bez knih
            using (var cmd = new FbCommand(@"DELETE FROM GENRES WHERE NOT EXISTS (SELECT 1 FROM KNIHY_GENRES kg
                                                WHERE kg.GENRE_ID = GENRES.GENRE_ID)", conn, tx))
            {
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        //
        private void CtxDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        //
        private void CtxEdit_Click(object sender, RoutedEventArgs e) => EditSelected();

        //
        private void DeleteAllAutor(FbConnection conn, FbTransaction tx)
        {
            using var cmd = new FbCommand(@"DELETE FROM AUTORY
                                            WHERE NOT EXISTS (
                                                SELECT 1 FROM KNIHY
                                                WHERE KNIHY.AUTOR_ID = AUTORY.ID)", conn, tx);
            cmd.ExecuteNonQuery();
        }

        private void DeleteBook_Click(object sender, RoutedEventArgs e)
        {
            if (TablKnihy.SelectedItem is not DataRowView row) return;

            int knihyID = Convert.ToInt32(row["ID"]);
            string? nazev = row["NAZEV"] as string;

            if (MessageBox.Show(
                $"Opravdu chcete smazat knihu: \"{nazev}\"?",
                "Potvrzení", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            DeleteBookCascade(knihyID);
            ReloadAllTable();
        }

        private void DeleteBookCascade(int knihyID)
        {
            using var conn = new FbConnection(_connString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            int? autorID = null;
            int? nakladID = null;
            string? coverPath = null;

            //
            using (var cmd = new FbCommand(
                "SELECT AUTOR_ID, NAKLADATELSTVI_ID FROM KNIHY WHERE ID=@id", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", knihyID);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    autorID = r.IsDBNull(0) ? null : r.GetInt32(0);
                    nakladID = r.IsDBNull(1) ? null : r.GetInt32(1);
                }
            }

            //
            using (var cmd = new FbCommand(
                "SELECT KNIHY_PATH FROM KNIHY_DETAILS WHERE KNIHYDET_ID=@id", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", knihyID);
                var o = cmd.ExecuteScalar();
                if (o != null && o != DBNull.Value) coverPath = Convert.ToString(o);
            }

            //
            using (var cmd = new FbCommand(
                "DELETE FROM KNIHY_GENRES WHERE KNIHY_ID=@id", conn, tx))
            { cmd.Parameters.AddWithValue("id", knihyID); cmd.ExecuteNonQuery(); }

            //
            using (var cmd = new FbCommand(
                "DELETE FROM KNIHY_DETAILS WHERE KNIHYDET_ID=@id", conn, tx))
            { cmd.Parameters.AddWithValue("id", knihyID); cmd.ExecuteNonQuery(); }

            //
            using (var cmd = new FbCommand(
                "DELETE FROM KNIHY WHERE ID=@id", conn, tx))
            { cmd.Parameters.AddWithValue("id", knihyID); cmd.ExecuteNonQuery(); }

            //smazat path do obrazovky
            var abs = string.IsNullOrWhiteSpace(coverPath) ? null
                : (Path.IsPathRooted(coverPath) ? coverPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, coverPath));

            if (!string.IsNullOrWhiteSpace(abs) && File.Exists(abs))
            {
                try { File.Delete(abs); } catch { /*ignore*/}
            }

            //kontrola na autora
            if (autorID.HasValue)
            {
                using var c = new FbCommand(
                    "SELECT COUNT(*) FROM KNIHY WHERE AUTOR_ID=@aid", conn, tx);
                c.Parameters.AddWithValue("aid", autorID.Value);
                if (Convert.ToInt32(c.ExecuteScalar()) == 0)
                {
                    using var d = new FbCommand("DELETE FROM AUTORY WHERE ID=@aid", conn, tx);
                    d.Parameters.AddWithValue("aid", autorID.Value);
                    d.ExecuteNonQuery();
                }
            }

            //kontrola na nakladatelstvi
            if (nakladID.HasValue)
            {
                using var c2 = new FbCommand(
                    "SELECT COUNT(*) FROM KNIHY WHERE NAKLADATELSTVI_ID=@nid", conn, tx);
                c2.Parameters.AddWithValue("nid", nakladID.Value);
                if (Convert.ToInt32(c2.ExecuteScalar()) == 0)
                {
                    using var d2 = new FbCommand("DELETE FROM NAKLADATELSTVI WHERE ID=@nid", conn, tx);
                    d2.Parameters.AddWithValue("nid", nakladID.Value);
                    d2.ExecuteNonQuery();
                }
            }
            DeleteAllAutor(conn, tx);
            tx.Commit();
        }

        //
        private void DeleteSelected()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            // візьмемо назву для підтвердження
            if (TablKnihy.SelectedItem is DataRowView row)
            {
                string? nazev = row["Nazev"] as string;
                if (MessageBox.Show(
                    $"Opravdu chcete smazat knihu: \"{nazev}\"?",
                    "Potvrzení", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            DeleteBookCascade(id.Value);
            ReloadAllTable();
        }

        //
        private void EditBook_Click(object sender, RoutedEventArgs e)
        {
            if (TablKnihy.SelectedItem is DataRowView row)
            {
                int knihyID = (int)row["ID"];
                var win = new KnihyDetailWindowAdd(knihyID);
                win.Owner = this;
                if (win.ShowDialog() == true)
                {
                    ReloadAllTable();
                }
            }
        }

        //
        private void EditSelected()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            var win = new KnihyDetailWindowAdd(id.Value);
            win.Owner = this;
            if (win.ShowDialog() == true)
                ReloadAllTable();
        }

        //
        private int? GetSelectedBookID()
        {
            if (TablKnihy.SelectedItem is DataRowView row)
            {
                return Convert.ToInt32(row["ID"]);
            }
            return null;
        }

        //
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
                //MessageBox.Show(
                //$"Nacitani dat z databaze uspesne dokonceno. \nPocet nactenych radku: {dt.Rows.Count}",
                //"Uspesne",
                //MessageBoxButton.OK,
                // MessageBoxImage.Information);
                grid.ItemsSource = dt.DefaultView;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chyba pri nacitani dat: \n {ex.Message}",
                    "Chyba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        //
        private void OpenDetailsForSelected()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            _openingDetails = true;
            try
            {
                var detailWin = new KnihyDetailWindow(id.Value);
                detailWin.Owner = this;
                detailWin.ShowDialog();
                //ReloadAllTable();
            }
            finally { _openingDetails = false; }
        }

        //
        private void ReloadAllTable()
        {
            _isReloading = true;
            try
            {
                LoadTable(
                    @"SELECT
                        k.ID AS ID,
                        k.NAZEV AS Nazev,
                        k.ROK AS Rok,
                        a.PRIJMENI || ' ' || a.JMENO AS Autor,
                        n.NAZEV_FIRMY AS Nakladatelstvi,
                        k.POCET_STRAN AS PocetStran
                      FROM KNIHY k
                      LEFT JOIN AUTORY a ON k.AUTOR_ID = a.ID
                      LEFT JOIN NAKLADATELSTVI n ON k.NAKLADATELSTVI_ID = n.ID
                      ORDER BY k.ID",
                    TablKnihy);

                LoadTable("SELECT * FROM AUTORY ORDER BY ID", TablAutoriDetail);
                LoadTable("SELECT * FROM NAKLADATELSTVI ORDER BY ID", TablNakladatelstviDetail);

                TablKnihy.SelectedItem = null;
                BtnPridatKnihu.IsEnabled = true;
                BtnUpravitKnihu.IsEnabled = false;
                BtnSmazatKnihu.IsEnabled = false;
            }
            finally { _isReloading = false; }
        }

        //
        private void TablKnihy_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (GetSelectedBookID() == null) return;
            OpenDetailsForSelected();
        }

        private void TablKnihy_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                EditSelected();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Delete)
            {
                DeleteSelected();
                e.Handled = true;
            }
        }

        private void TablKnihy_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isReloading || _openingDetails) return;

            bool hasSel = TablKnihy.SelectedItem is DataRowView;
            BtnUpravitKnihu.IsEnabled = hasSel;
            BtnSmazatKnihu.IsEnabled = hasSel;
        }
    }
}