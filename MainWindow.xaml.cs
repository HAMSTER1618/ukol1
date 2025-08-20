using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;   // Hyperlink
using System.Windows.Input;
using FirebirdSql.Data.FirebirdClient;
using static ukol1.Manager;
using Microsoft.VisualBasic;

namespace ukol1
{
    public partial class MainWindow : Window
    {
        private bool _isReloading;
        private bool _openingDetails;

        public MainWindow()
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // On startup: clean orphan rows and then load all tables.
            Loaded += async (s, e) =>
            {
                await Manager.CleanDbOrphansAsync();
                await ReloadAllTableAsync();
            };

            // Context menu actions.
            CtxEdit.Click += async (s, e) => await EditSelectedAsync();
            CtxDelete.Click += async (s, e) => await DeleteSelectedAsync();
        }

        //Generic helper: run SELECT -> DataView off the UI thread
        //so the window stays responsive.

        private static Task<DataView> LoadDataViewAsync(string sql)
        {
            return Task.Run(() =>
            {
                using var conn = new FbConnection(Manager.ConnectionString);
                using var cmd = new FbCommand(sql, conn);
                using var da = new FbDataAdapter(cmd);
                var dt = new DataTable();
                conn.Open();
                da.Fill(dt);
                return dt.DefaultView;
            });
        }

        // Add new Author
        /*private async void AddAuthor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AuthorEditWindow { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                await Manager.InsertAuthorAsync(dlg.LastName, dlg.FirstName);
                await ReloadAllTableAsync();
            }
        }*/

        //Add new Book
        private async void AddBook_Click(object sender, RoutedEventArgs e)
        {
            var win = new KnihyDetailWindowAdd { Owner = this };
            if (win.ShowDialog() == true)
                await ReloadAllTableAsync();
        }

        // Add new Publisher
        /*private async void AddPublisher_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PublisherEditWindow { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                await Manager.InsertPublisherAsync(dlg.PublisherName, dlg.City);
                await ReloadAllTableAsync();
            }
        }*/

        private async void DeleteBook_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

        //Delete the selected book (after confirmation) and reload all tables.
        //Works both for DataRowView and strongly-typed BookRow.

        private async Task DeleteSelectedAsync()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            // Try to get a human-friendly name for the confirmation dialog.
            string? name = TableBooks.SelectedItem switch
            {
                DataRowView drv => drv["Nazev"] as string,
                Manager.BookRow br => br.Nazev,
                _ => null
            };

            var msg = !string.IsNullOrWhiteSpace(name)
                ? $"Opravdu chcete smazat knihu: \"{name}\"?"
                : "Opravdu chcete smazat vybranou knihu?";

            if (MessageBox.Show(msg, "Potvrzení",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            await Manager.DeleteBookCascadeAsync(id.Value);
            await ReloadAllTableAsync();
        }

        //Toolbar edit button – open dialog for the selected book; then reload.

        private async void EditBook_Click(object sender, RoutedEventArgs e) => await EditSelectedAsync();

        //Open edit dialog for the selected book; after closing, reload data.

        private async Task EditSelectedAsync()
        {
            var id = GetSelectedBookID();
            if (id == null) return;

            var win = new KnihyDetailWindowAdd(id.Value) { Owner = this };
            if (win.ShowDialog() == true)
                await ReloadAllTableAsync();
        }

        //Get ID of the currently selected book (supports DataRowView and BookRow).

        private int? GetSelectedBookID()
        {
            var item = TableBooks.SelectedItem;
            if (item == null) return null;

            if (item is DataRowView drv)
                return Convert.ToInt32(drv["ID"]);

            if (item is Manager.BookRow br)
                return br.ID;

            // Fallback via reflection if some other type appears.
            var prop = item.GetType().GetProperty("ID");
            var val = prop?.GetValue(item);
            return val != null ? Convert.ToInt32(val) : (int?)null;
        }

        private async void OpenBookLink_Click(object sender, RoutedEventArgs e)
        {
            int id;
            if (sender is Hyperlink h)
            {
                if (h.DataContext is Manager.BookRow br) id = br.ID;
                else if (h.DataContext is DataRowView drv) id = Convert.ToInt32(drv["ID"]);
                else return;
            }
            else return;

            // Open details and then reload.
            var win = new KnihyDetailWindow(id) { Owner = this };
            win.ShowDialog();
            await ReloadAllTableAsync();
        }

        // Open details for the currently selected row (re-entrancy-safe).

        private async Task OpenDetailsForSelected()
        {
            var id = GetSelectedBookID();
            if (id == null || _openingDetails) return;

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

        //Reload books, authors and publishers into their grids.

        private async Task ReloadAllTableAsync()
        {
            _isReloading = true;
            try
            {
                // Books: load as a typed list and force grid redraw.
                var books = await Manager.GetBooksAsync();
                TableBooks.ItemsSource = null;
                TableBooks.ItemsSource = books;

                // Authors and publishers: load as DataView on a background thread.
                TableAuthor.ItemsSource = await LoadDataViewAsync("SELECT * FROM AUTORY ORDER BY ID");
                TablePubl.ItemsSource = await LoadDataViewAsync("SELECT * FROM NAKLADATELSTVI ORDER BY ID");

                // Reset selection and buttons state.
                TableBooks.SelectedItem = null;
                BtnAddBook.IsEnabled = true;
                BtnEditBook.IsEnabled = false;
                BtnDeleteBook.IsEnabled = false;
            }
            finally
            {
                _isReloading = false;
            }
        }

        // Edit Author row
        private async void RowEditAuthorRow_Click(object sender, RoutedEventArgs e)
        {
            var drv = (sender as FrameworkElement)?.Tag as DataRowView
                      ?? TableAuthor.SelectedItem as DataRowView;
            if (drv == null) return;

            var id = Convert.ToInt32(drv["ID"]);
            var last = drv.Row.Table.Columns.Contains("PRIJMENI") ? drv["PRIJMENI"]?.ToString() ?? "" : "";
            var first = drv.Row.Table.Columns.Contains("JMENO") ? drv["JMENO"]?.ToString() ?? "" : "";

            var dlg = new AuthorEditWindow(last, first) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                await Manager.UpdateAuthorAsync(id, dlg.LastName, dlg.FirstName);
                await ReloadAllTableAsync();
            }
        }

        // Left-click on a row: open details.
        private async void RowEditBook_Click(object sender, RoutedEventArgs e)
        {
            var rowItem = (sender as FrameworkElement)?.Tag;
            if (rowItem == null) return;

            // temporarily select that row and reuse existing logic
            TableBooks.SelectedItem = rowItem;
            await EditSelectedAsync();
        }

        // Edit Publisher row (name + city)
        private async void RowEditPublisherRow_Click(object sender, RoutedEventArgs e)
        {
            var drv = (sender as FrameworkElement)?.Tag as DataRowView
                      ?? TablePubl.SelectedItem as DataRowView;
            if (drv == null) return;

            var id = Convert.ToInt32(drv["ID"]);
            var name = drv.Row.Table.Columns.Contains("NAZEV_FIRMY") ? drv["NAZEV_FIRMY"]?.ToString() ?? "" : "";
            var city = drv.Row.Table.Columns.Contains("MESTO") ? drv["MESTO"]?.ToString() ?? "" : "";

            var dlg = new PublisherEditWindow(name, city) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                await Manager.UpdatePublisherAsync(id, dlg.PublisherName, dlg.City);
                await ReloadAllTableAsync();
            }
        }

        private async void TableBooks_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isReloading || _openingDetails) return;
            await OpenDetailsForSelected();
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
            if (_isReloading || _openingDetails) return;

            var hasSel = TableBooks.SelectedItem != null;
            BtnEditBook.IsEnabled = hasSel;
            BtnDeleteBook.IsEnabled = hasSel;
        }
    }
}