//Manager.cs
using System.Configuration;
using System.Data;
using System.IO;
using FirebirdSql.Data.FirebirdClient;

namespace ukol1
{
    public static class Manager
    {
        //Single place for connection string
        private static readonly string _connString =
            ConfigurationManager.ConnectionStrings["KNIHOVNA2"].ConnectionString;

        public static string ConnectionString => _connString;

        //Removes orphaned rows: authors, publishers, and genres not referenced by any book.
        public static void CleanDbOrphans()
        {
            using var conn = CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            //authors without books
            using (var cmd = new FbCommand(@"
                DELETE FROM AUTORY
                WHERE NOT EXISTS (
                    SELECT 1 FROM KNIHY WHERE KNIHY.AUTOR_ID = AUTORY.ID
                )", conn, tx))
            {
                cmd.ExecuteNonQuery();
            }

            //publishers without books
            using (var cmd = new FbCommand(@"
                DELETE FROM NAKLADATELSTVI
                WHERE NOT EXISTS (
                    SELECT 1 FROM KNIHY WHERE KNIHY.NAKLADATELSTVI_ID = NAKLADATELSTVI.ID
                )", conn, tx))
            {
                cmd.ExecuteNonQuery();
            }

            //genres without books
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

        //===================== Public API =====================

        public static Task CleanDbOrphansAsync() => Task.Run(CleanDbOrphans);

        //Deletes a book and related data (book-genre links, details, cover file).
        //Also removes orphaned author/publisher afterwards if they are no longer referenced.

        public static async Task DeleteAuthorWithBooksAsync(int authorId)
        {
            // 1) Collect all book IDs by this author
            var bookIds = new List<int>();
            await using (var conn = new FbConnection(ConnectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                await using var cmd = new FbCommand(
                    "SELECT ID FROM KNIHY WHERE AUTOR_ID=@aid", conn);
                cmd.Parameters.AddWithValue("aid", authorId);

                await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await r.ReadAsync().ConfigureAwait(false))
                    bookIds.Add(r.GetInt32(0));
            }

            // 2) Delete each book via existing cascade helper (removes links, details, covers)
            foreach (var bid in bookIds)
                await DeleteBookCascadeAsync(bid).ConfigureAwait(false);

            // 3) Finally remove the author record (may already be removed as orphan)
            await using (var conn2 = new FbConnection(ConnectionString))
            {
                await conn2.OpenAsync().ConfigureAwait(false);
                await using var cmd2 = new FbCommand(
                    "DELETE FROM AUTORY WHERE ID=@id", conn2);
                cmd2.Parameters.AddWithValue("id", authorId);
                await cmd2.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        public static async Task DeleteBookCascadeAsync(string connStr, int bookID, Func<string?, string?> toAbsolutePath)
        {
            using var conn = new FbConnection(connStr);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync().ConfigureAwait(false);

            var authorID = await Db.ScalarAsync<int?>(conn, tx,
                "SELECT AUTOR_ID FROM KNIHY WHERE ID=@id", ("id", bookID)).ConfigureAwait(false);

            var pubID = await Db.ScalarAsync<int?>(conn, tx,
                "SELECT NAKLADATELSTVI_ID FROM KNIHY WHERE ID=@id", ("id", bookID)).ConfigureAwait(false);

            var coverPath = await Db.ScalarAsync<string?>(conn, tx,
                "SELECT KNIHY_CESTA FROM KNIHY_DETAILY WHERE KNIHYDET_ID=@id", ("id", bookID)).ConfigureAwait(false);

            await Db.NonQueryAsync(conn, tx, "DELETE FROM KNIHY_ZANRY   WHERE KNIHY_ID=@id", ("id", bookID)).ConfigureAwait(false);
            await Db.NonQueryAsync(conn, tx, "DELETE FROM KNIHY_DETAILY WHERE KNIHYDET_ID=@id", ("id", bookID)).ConfigureAwait(false);
            await Db.NonQueryAsync(conn, tx, "DELETE FROM KNIHY         WHERE ID=@id", ("id", bookID)).ConfigureAwait(false);

            await tx.CommitAsync().ConfigureAwait(false);

            //delete cover file (best-effort)
            var abs = toAbsolutePath?.Invoke(coverPath) ?? Db.PathUtils.ToAbsolutePath(coverPath);
            Db.TryDeleteFile(abs);

            //remove orphan author
            if (authorID.HasValue)
            {
                using var c = new FbConnection(connStr);
                await c.OpenAsync().ConfigureAwait(false);
                var cnt = await Db.ScalarAsync<int>(c, null,
                    "SELECT COUNT(*) FROM KNIHY WHERE AUTOR_ID=@aid", ("aid", authorID.Value)).ConfigureAwait(false);
                if (cnt == 0)
                    await Db.NonQueryAsync(c, null, "DELETE FROM AUTORY WHERE ID=@aid", ("aid", authorID.Value)).ConfigureAwait(false);
            }

            //remove orphan publisher
            if (pubID.HasValue)
            {
                using var c = new FbConnection(connStr);
                await c.OpenAsync().ConfigureAwait(false);
                var cnt = await Db.ScalarAsync<int>(c, null,
                    "SELECT COUNT(*) FROM KNIHY WHERE NAKLADATELSTVI_ID=@nid", ("nid", pubID.Value)).ConfigureAwait(false);
                if (cnt == 0)
                    await Db.NonQueryAsync(c, null, "DELETE FROM NAKLADATELSTVI WHERE ID=@nid", ("nid", pubID.Value)).ConfigureAwait(false);
            }
        }

        //---------------- Book delete (cascade) ----------------
        //Shortcut that uses Manager's connection string and the default <see cref="ToAbsolute"/> resolver.
        public static Task DeleteBookCascadeAsync(int bookID)
            => DeleteBookCascadeAsync(_connString, bookID, ToAbsolute);

        public static async Task DeletePublisherAndDetachBooksAsync(int publisherId)
        {
            await using var conn = new FbConnection(ConnectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync().ConfigureAwait(false);

            //Detach this publisher from all books (keep books)
            await using (var cmd = new FbCommand(
                "UPDATE KNIHY SET NAKLADATELSTVI_ID=NULL WHERE NAKLADATELSTVI_ID=@nid", conn, tx))
            {
                cmd.Parameters.AddWithValue("nid", publisherId);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            //Delete the publisher record
            await using (var cmd2 = new FbCommand(
                "DELETE FROM NAKLADATELSTVI WHERE ID=@nid", conn, tx))
            {
                cmd2.Parameters.AddWithValue("nid", publisherId);
                await cmd2.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await tx.CommitAsync().ConfigureAwait(false);
        }

        //---------------- Book details (async) ----------------
        //Loads book details by id (async)
        public static async Task<BookDetails?> GetBookDetailsAsync(string connStr, int id)
        {
            const string sql = @"
                SELECT
                  k.NAZEV AS Nazev,
                  k.ROK AS Rok,
                  k.POCET_STRAN AS PocetStran,
                  TRIM(COALESCE(a.PRIJMENI,'') || ' ' || COALESCE(a.JMENO,'')) AS Autor,
                  CASE
                    WHEN TRIM(COALESCE(n.NAZEV_FIRMY, '')) <> '' AND TRIM(COALESCE(n.MESTO, '')) <> ''
                         THEN TRIM(COALESCE(n.NAZEV_FIRMY, '')) || ', ' || TRIM(COALESCE(n.MESTO, ''))
                    WHEN TRIM(COALESCE(n.NAZEV_FIRMY, '')) <> ''
                         THEN TRIM(COALESCE(n.NAZEV_FIRMY, ''))
                    WHEN TRIM(COALESCE(n.MESTO, '')) <> ''
                         THEN TRIM(COALESCE(n.MESTO, ''))
                    ELSE ''
                  END AS Nakladatelstvi,
                  COALESCE(d.POPIS_KNIHY,'')  AS PopisKnihy,
                  COALESCE(d.KNIHY_CESTA,'')  AS KnihyCesta
                FROM KNIHY k
                LEFT JOIN AUTORY a ON k.AUTOR_ID = a.ID
                LEFT JOIN NAKLADATELSTVI n ON k.NAKLADATELSTVI_ID = n.ID
                LEFT JOIN KNIHY_DETAILY d ON k.ID = d.KNIHYDET_ID
                WHERE k.ID = @id";

            using var conn = new FbConnection(connStr);
            await conn.OpenAsync().ConfigureAwait(false);
            using var cmd = new FbCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false);
            if (!await r.ReadAsync().ConfigureAwait(false)) return null;

            return new BookDetails
            {
                Nazev = Convert.ToString(r["Nazev"]) ?? "",
                Rok = r["Rok"] == DBNull.Value ? null : Convert.ToInt32(r["Rok"]),
                PocetStran = r["PocetStran"] == DBNull.Value ? null : Convert.ToInt32(r["PocetStran"]),
                Autor = Convert.ToString(r["Autor"]) ?? "",
                Nakladatelstvi = Convert.ToString(r["Nakladatelstvi"]) ?? "",
                PopisKnihy = Convert.ToString(r["PopisKnihy"]) ?? "",
                KnihyCesta = Convert.ToString(r["KnihyCesta"]) ?? ""
            };
        }

        public static Task<BookDetails?> GetBookDetailsAsync(int id) => GetBookDetailsAsync(_connString, id);

        //Returns books for the main grid
        public static async Task<List<BookRow>> GetBooksAsync()
        {
            var list = new List<BookRow>();
            using var conn = CreateConnection();
            await conn.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT
                  k.ID,
                  k.NAZEV,
                  TRIM(COALESCE(a.PRIJMENI,'') || ' ' || COALESCE(a.JMENO,'')) AS AUTOR,
                  COALESCE(n.NAZEV_FIRMY,'') AS NAKLADATELSTVI,
                  k.ROK,
                  k.POCET_STRAN
                FROM KNIHY k
                LEFT JOIN AUTORY a ON k.AUTOR_ID = a.ID
                LEFT JOIN NAKLADATELSTVI n ON k.NAKLADATELSTVI_ID = n.ID
                ORDER BY k.ID";

            using var cmd = new FbCommand(sql, conn);
            using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await r.ReadAsync().ConfigureAwait(false))
            {
                list.Add(new BookRow
                {
                    ID = r.GetInt32(0),
                    Nazev = await r.IsDBNullAsync(1).ConfigureAwait(false) ? "" : r.GetString(1),
                    Autor = await r.IsDBNullAsync(2).ConfigureAwait(false) ? "-" : r.GetString(2),
                    Nakladatelstvi = await r.IsDBNullAsync(3).ConfigureAwait(false) ? "-" : r.GetString(3),
                    Rok = await r.IsDBNullAsync(4).ConfigureAwait(false) ? (int?)null : r.GetInt32(4),
                    PocetStran = await r.IsDBNullAsync(5).ConfigureAwait(false) ? (int?)null : r.GetInt32(5),
                });
            }
            return list;
        }

        //Inserts a new book or updates an existing one. Also upserts details and genres,

        public static async Task<int> SaveOrUpdateBookAsync(
                                            string connStr,
            int? bookID,
            string name,
            string? authorName,
            string? authorSurname,
            string? publName,
            string? publCity,
            int? year,
            int? numberOfPages,
            string? descr,
            string? selectedFilePath,
            Func<string, int, string> storeCoverFile,
            Func<string?, string?> toAbsolutePath,
            string? genresCsv)
        {
            using var conn = new FbConnection(connStr);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync().ConfigureAwait(false);

            //normalize text inputs
            var jm = (authorName ?? string.Empty).Trim();
            var pr = (authorSurname ?? string.Empty).Trim();
            var pn = (publName ?? string.Empty).Trim();
            var pc = (publCity ?? string.Empty).Trim();

            int bookId;

            if (bookID == null)
            {
                //NEW book: find or create related rows
                int authorId = await GetOrCreateAuthorAsync(conn, tx, jm, pr).ConfigureAwait(false);

                int? publisherID = null;
                if (!string.IsNullOrWhiteSpace(pn) || !string.IsNullOrWhiteSpace(pc))
                    publisherID = await GetOrCreatePublAsync(conn, tx, pn, pc).ConfigureAwait(false);

                //Insert without explicit ID; Firebird identity will generate it.
                bookId = await Db.ScalarAsync<int>(conn, tx, @"
                        INSERT INTO KNIHY (NAZEV, ROK, AUTOR_ID, NAKLADATELSTVI_ID, POCET_STRAN)
                        VALUES (@nazev, @rok, @aid, @nid, @ps)
                        RETURNING ID",
                    ("nazev", name), ("rok", year),
                    ("aid", authorId), ("nid", publisherID), ("ps", numberOfPages)
                ).ConfigureAwait(false);
            }
            else
            {
                //EDIT existing book: keep same author/publisher IDs; just update their rows
                bookId = bookID.Value;

                //update book core fields
                await Db.NonQueryAsync(conn, tx, @"
                    UPDATE KNIHY SET
                      NAZEV=@nazev,
                      ROK=@rok,
                      POCET_STRAN=@ps
                    WHERE ID=@id",
                    ("id", bookId), ("nazev", name), ("rok", year), ("ps", numberOfPages)).ConfigureAwait(false);

                //get current FKs
                int currentAuthorId = await Db.ScalarAsync<int>(conn, tx, "SELECT AUTOR_ID FROM KNIHY WHERE ID=@id", ("id", bookId)).ConfigureAwait(false);
                int? currentPublId = await Db.ScalarAsync<int?>(conn, tx, "SELECT NAKLADATELSTVI_ID FROM KNIHY WHERE ID=@id", ("id", bookId)).ConfigureAwait(false);

                //update existing author (NO create/delete on edit)
                await Db.NonQueryAsync(conn, tx,
                    "UPDATE AUTORY SET PRIJMENI=@pr, JMENO=@jm WHERE ID=@aid",
                    ("pr", pr), ("jm", jm), ("aid", currentAuthorId)).ConfigureAwait(false);

                //update publisher per user input:
                if (string.IsNullOrWhiteSpace(pn) && string.IsNullOrWhiteSpace(pc))
                {
                    //user cleared publisher -> detach from book (do NOT delete the publisher row)
                    await Db.NonQueryAsync(conn, tx,
                        "UPDATE KNIHY SET NAKLADATELSTVI_ID = NULL WHERE ID=@id",
                        ("id", bookId)).ConfigureAwait(false);
                }
                else if (currentPublId.HasValue)
                {
                    //publisher already linked -> update its row
                    await Db.NonQueryAsync(conn, tx,
                        "UPDATE NAKLADATELSTVI SET NAZEV_FIRMY=@n, MESTO=@m WHERE ID=@nid",
                        ("n", pn), ("m", pc), ("nid", currentPublId.Value)).ConfigureAwait(false);
                }
                else
                {
                    //no publisher linked yet -> create/find and link
                    int newPublId = await GetOrCreatePublAsync(conn, tx, pn, pc).ConfigureAwait(false);
                    await Db.NonQueryAsync(conn, tx,
                        "UPDATE KNIHY SET NAKLADATELSTVI_ID=@nid WHERE ID=@id",
                        ("nid", newPublId), ("id", bookId)).ConfigureAwait(false);
                }
            }

            //details & cover
            var oldRelPath = await Db.ScalarAsync<string?>(conn, tx,
                "SELECT KNIHY_CESTA FROM KNIHY_DETAILY WHERE KNIHYDET_ID=@id", ("id", bookId)).ConfigureAwait(false);

            string? newRelPath = oldRelPath;
            string? oldAbsToDelete = null;

            if (!string.IsNullOrWhiteSpace(selectedFilePath))
            {
                newRelPath = storeCoverFile(selectedFilePath!, bookId);
                if (!string.IsNullOrWhiteSpace(oldRelPath) &&
                    !string.Equals(oldRelPath, newRelPath, StringComparison.OrdinalIgnoreCase))
                {
                    oldAbsToDelete = toAbsolutePath(oldRelPath);
                }
            }

            await UpsertDetailsAsync(conn, tx, bookId, descr, newRelPath).ConfigureAwait(false);
            await SetGenresAsync(conn, tx, bookId, genresCsv).ConfigureAwait(false);

            await tx.CommitAsync().ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(oldAbsToDelete) && File.Exists(oldAbsToDelete))
            {
                try { File.Delete(oldAbsToDelete); } catch { /* ignore */ }
            }

            return bookId;
        }

        //---------------- Books list for main grid ----------------
        //---------------- Save / update book ----------------

        public static Task<int> SaveOrUpdateBookAsync(
            int? bookID,
            string name,
            string? authorName,
            string? authorSurname,
            string? publName,
            string? publCity,
            int? year,
            int? numberOfPages,
            string? desc,
            string? selectedFilePath,
            Func<string, int, string> storeCoverFile,
            Func<string?, string?> toAbsolutePath,
            string? genresCsv)
            => SaveOrUpdateBookAsync(_connString, bookID, name, authorName, authorSurname,
                                     publName, publCity, year, numberOfPages, desc,
                                     selectedFilePath, storeCoverFile, toAbsolutePath, genresCsv);

        //Converts a DB-stored relative path (e.g. "Images/cover.png") to an absolute filesystem path.
        public static string? ToAbsolute(string? dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath)) return null;
            return Path.IsPathRooted(dbPath)
                ? dbPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
        }

        //--- AUTHORS ---
        public static async Task UpdateAuthorAsync(int id, string surname, string name)
        {
            //normalize inputs
            var sr = (surname ?? string.Empty).Trim();
            var nm = (name ?? string.Empty).Trim();

            using var conn = new FbConnection(_connString);
            await conn.OpenAsync().ConfigureAwait(false);

            using var cmd = new FbCommand(
                "UPDATE AUTORY SET PRIJMENI=@p, JMENO=@j WHERE ID=@id", conn);

            //keep parameter names consistent with the rest of the project
            cmd.Parameters.AddWithValue("p", sr);
            cmd.Parameters.AddWithValue("j", nm);
            cmd.Parameters.AddWithValue("id", id);

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        //--- PUBLISHERS ---
        public static async Task UpdatePublisherAsync(int id, string name, string city)
        {
            //normalize inputs
            var n = (name ?? string.Empty).Trim();
            var c = (city ?? string.Empty).Trim();

            using var conn = new FbConnection(_connString);
            await conn.OpenAsync().ConfigureAwait(false);

            using var cmd = new FbCommand(
                "UPDATE NAKLADATELSTVI SET NAZEV_FIRMY=@n, MESTO=@m WHERE ID=@id", conn);
            //keep parameter names consistent with the rest of the project
            cmd.Parameters.AddWithValue("n", n);
            cmd.Parameters.AddWithValue("m", c);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static FbConnection CreateConnection() => new FbConnection(_connString);

        //===================== Private helpers =====================

        //find-or-create author
        private static async Task<int> GetOrCreateAuthorAsync(
            FbConnection conn, FbTransaction tx, string? name, string? surname)
        {
            var nm = (name ?? string.Empty).Trim();
            var sr = (surname ?? string.Empty).Trim();

            //Try to find existing author
            var existing = await Db.ScalarAsync<int?>(conn, tx,
                "SELECT FIRST 1 ID FROM AUTORY " +
                "WHERE UPPER(TRIM(PRIJMENI)) = UPPER(@pr) " +
                "  AND UPPER(TRIM(JMENO))    = UPPER(@jm)",
                ("pr", sr), ("jm", nm));

            if (existing.HasValue) return existing.Value;

            //Insert without ID; Firebird trigger fills it. Get it back via RETURNING.
            var newId = await Db.ScalarAsync<int>(conn, tx,
                "INSERT INTO AUTORY (PRIJMENI, JMENO) VALUES (@pr, @jm) RETURNING ID",
                ("pr", sr), ("jm", nm));

            return newId;
        }

        //find-or-create publisher
        private static async Task<int> GetOrCreatePublAsync(
            FbConnection conn, FbTransaction tx, string? name, string? city)
        {
            var n = (name ?? string.Empty).Trim();
            var m = (city ?? string.Empty).Trim();

            var existing = await Db.ScalarAsync<int?>(conn, tx,
                "SELECT FIRST 1 ID FROM NAKLADATELSTVI " +
                "WHERE UPPER(TRIM(NAZEV_FIRMY))=UPPER(@n) " +
                "  AND UPPER(TRIM(COALESCE(MESTO,'')))=UPPER(@m)",
                ("n", n), ("m", m)).ConfigureAwait(false);

            if (existing.HasValue) return existing.Value;

            //Insert without explicit ID; identity generates it. Read it back.
            var newId = await Db.ScalarAsync<int>(conn, tx,
                "INSERT INTO NAKLADATELSTVI (NAZEV_FIRMY, MESTO) VALUES (@n, @m) RETURNING ID",
                ("n", n), ("m", m)).ConfigureAwait(false);

            return newId;
        }

        //set genres for a book from a CSV string
        private static async Task SetGenresAsync(FbConnection conn, FbTransaction tx, int bookId, string? csv)
        {
            await Db.NonQueryAsync(conn, tx, "DELETE FROM KNIHY_ZANRY WHERE KNIHY_ID=@id", ("id", bookId)).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(csv)) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim();
                if (name.Length == 0) continue;
                if (!seen.Add(name)) continue;

                var existing = await Db.ScalarAsync<int?>(conn, tx,
                    "SELECT FIRST 1 ZANRY_ID FROM ZANRY WHERE UPPER(TRIM(NAZEV_ZANRY))=UPPER(@n)",
                    ("n", name)).ConfigureAwait(false);

                int gid = existing ?? await Db.ScalarAsync<int>(conn, tx,
                    "INSERT INTO ZANRY (NAZEV_ZANRY) VALUES (@n) RETURNING ZANRY_ID",
                    ("n", name)).ConfigureAwait(false);

                await Db.NonQueryAsync(conn, tx,
                    "INSERT INTO KNIHY_ZANRY (KNIHY_ID, ZANRY_ID) VALUES (@bid, @gid)",
                    ("bid", bookId), ("gid", gid)).ConfigureAwait(false);
            }
        }

        //insert or update details row
        private static async Task UpsertDetailsAsync(FbConnection conn, FbTransaction tx, int bookId, string? desc, string? relativePath)
        {
            var updated = await Db.NonQueryAsync(conn, tx,
                "UPDATE KNIHY_DETAILY SET POPIS_KNIHY=@p, KNIHY_CESTA=@cesta WHERE KNIHYDET_ID=@id",
                ("p", desc), ("cesta", relativePath), ("id", bookId)).ConfigureAwait(false);

            if (updated == 0)
            {
                await Db.NonQueryAsync(conn, tx,
                    "INSERT INTO KNIHY_DETAILY (KNIHYDET_ID, POPIS_KNIHY, KNIHY_CESTA) VALUES (@id, @p, @cesta)",
                    ("id", bookId), ("p", desc), ("cesta", relativePath)).ConfigureAwait(false);
            }
        }

        //===================== DTOs =====================

        public sealed class BookDetails
        {
            public string Autor { get; set; } = "";
            public string KnihyCesta { get; set; } = "";
            public string Nakladatelstvi { get; set; } = "";
            public string Nazev { get; set; } = "";
            public int? PocetStran { get; set; }
            public string PopisKnihy { get; set; } = "";
            public int? Rok { get; set; }
        }

        public sealed class BookRow
        {
            public string Autor { get; set; } = "";
            public int ID { get; set; }
            public string Nakladatelstvi { get; set; } = "";
            public string Nazev { get; set; } = "";
            public int? PocetStran { get; set; }
            public int? Rok { get; set; }
        }

        //===================== Low-level DB helpers =====================

        internal static class Db
        {
            //Create FbCommand with parameters
            public static FbCommand Cmd(FbConnection conn, FbTransaction? tx, string sql,
                                        params (string Name, object? Value)[] ps)
            {
                var cmd = tx is null ? new FbCommand(sql, conn) : new FbCommand(sql, conn, tx);
                foreach (var (n, v) in ps)
                    cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
                return cmd;
            }

            //ExecuteNonQuery
            public static int NonQuery(FbConnection conn, FbTransaction? tx, string sql,
                                       params (string, object?)[] ps)
            {
                using var cmd = Cmd(conn, tx, sql, ps);
                return cmd.ExecuteNonQuery();
            }

            public static async Task<int> NonQueryAsync(FbConnection conn, FbTransaction? tx, string sql,
                                                        params (string, object?)[] ps)
            {
                using var cmd = Cmd(conn, tx, sql, ps);
                return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            //ExecuteReader single-row + mapper (async)
            public static async Task<T?> ReadSingleAsync<T>(FbConnection conn, FbTransaction? tx, string sql,
                                                            Func<IDataRecord, T> map,
                                                            params (string, object?)[] ps)
            {
                using var cmd = Cmd(conn, tx, sql, ps);
                using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false);
                return await r.ReadAsync().ConfigureAwait(false) ? map(r) : default;
            }

            //ExecuteScalar that returns default for null/DBNull
            public static T? Scalar<T>(FbConnection conn, FbTransaction? tx, string sql,
                                       params (string, object?)[] ps)
            {
                using var cmd = Cmd(conn, tx, sql, ps);
                var o = cmd.ExecuteScalar();
                if (o == null || o == DBNull.Value) return default;
                var t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                return (T)Convert.ChangeType(o, t);
            }

            public static async Task<T?> ScalarAsync<T>(FbConnection conn, FbTransaction? tx, string sql,
                                                        params (string, object?)[] ps)
            {
                using var cmd = Cmd(conn, tx, sql, ps);
                var o = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                if (o == null || o == DBNull.Value) return default;
                var t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                return (T)Convert.ChangeType(o, t);
            }

            //Safe file delete (best-effort)
            public static void TryDeleteFile(string? absPath)
            {
                if (!string.IsNullOrWhiteSpace(absPath) && File.Exists(absPath))
                { try { File.Delete(absPath); } catch { /* ignore */ } }
            }

            //Relative to absolute path
            public static class PathUtils
            {
                public static string? ToAbsolutePath(string? dbPath, string? baseDir = null)
                {
                    if (string.IsNullOrWhiteSpace(dbPath))
                        return null;

                    if (Path.IsPathRooted(dbPath))
                        return dbPath;

                    baseDir ??= AppDomain.CurrentDomain.BaseDirectory;
                    return Path.Combine(baseDir, dbPath);
                }
            }
        }
    }
}