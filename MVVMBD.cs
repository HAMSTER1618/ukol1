//MVVMBD.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using FirebirdSql.Data.FirebirdClient;
using System.Threading.Tasks;

namespace ukol1
{
    public static class MvvmBD
    {
        //verejne API

        // smazani knihy (vcetne orphanu)
        public static async Task DeleteBookCascadeAsync(string connString, int knihyID, Func<string?, string?> toAbsolutePath)
        {
            using var conn = new FbConnection(connString);
            await conn.OpenAsync().ConfigureAwait(false);
            using var tx = conn.BeginTransaction();

            var autorID = await Db.ScalarAsync<int?>(conn, tx, "SELECT AUTOR_ID FROM KNIHY WHERE ID=@id", ("id", knihyID)).ConfigureAwait(false);
            var nakladID = await Db.ScalarAsync<int?>(conn, tx, "SELECT NAKLADATELSTVI_ID FROM KNIHY WHERE ID=@id", ("id", knihyID)).ConfigureAwait(false);
            var coverPath = await Db.ScalarAsync<string?>(conn, tx, "SELECT KNIHY_CESTA FROM KNIHY_DETAILY WHERE KNIHYDET_ID=@id", ("id", knihyID)).ConfigureAwait(false);

            await Db.NonQueryAsync(conn, tx, "DELETE FROM KNIHY_ZANRY   WHERE KNIHY_ID=@id", ("id", knihyID)).ConfigureAwait(false);
            await Db.NonQueryAsync(conn, tx, "DELETE FROM KNIHY_DETAILY WHERE KNIHYDET_ID=@id", ("id", knihyID)).ConfigureAwait(false);
            await Db.NonQueryAsync(conn, tx, "DELETE FROM KNIHY         WHERE ID=@id", ("id", knihyID)).ConfigureAwait(false);

            tx.CommitAsync();

            var abs = toAbsolutePath?.Invoke(coverPath) ?? Db.ToAbsolute(coverPath);
            Db.TryDeleteFile(abs);

            if (autorID.HasValue)
            {
                using var c = new FbConnection(connString);
                await c.OpenAsync().ConfigureAwait(false);
                if ((await Db.ScalarAsync<int>(c, null, "SELECT COUNT(*) FROM KNIHY WHERE AUTOR_ID=@aid", ("aid", autorID.Value)).ConfigureAwait(false)) == 0)
                    await Db.NonQueryAsync(c, null, "DELETE FROM AUTORY WHERE ID=@aid", ("aid", autorID.Value)).ConfigureAwait(false);
            }
            if (nakladID.HasValue)
            {
                using var c = new FbConnection(connString);
                await c.OpenAsync().ConfigureAwait(false);
                if ((await Db.ScalarAsync<int>(c, null, "SELECT COUNT(*) FROM KNIHY WHERE NAKLADATELSTVI_ID=@nid", ("nid", nakladID.Value)).ConfigureAwait(false)) == 0)
                    await Db.NonQueryAsync(c, null, "DELETE FROM NAKLADATELSTVI WHERE ID=@nid", ("nid", nakladID.Value)).ConfigureAwait(false);
            }
        }

        // zkratka bez delegata (stejne jako u sync)
        public static Task DeleteBookCascadeAsync(string connString, int knihyID)
            => DeleteBookCascadeAsync(connString, knihyID, ToAbsolute);

        public static BookDetails? GetBookDetails(string connString, int id)
        {
            const string sql = @"
                SELECT
                  k.NAZEV AS Nazev,
                  k.ROK AS Rok,
                  k.POCET_STRAN AS PocetStran,
                  TRIM(COALESCE(a.PRIJMENI,'') || ' ' || COALESCE(a.JMENO,'')) AS Autor,
                  TRIM(COALESCE(n.NAZEV_FIRMY,'')) || ', ' || TRIM(COALESCE(n.MESTO,'')) AS Nakladatelstvi,
                  COALESCE(d.POPIS_KNIHY,'')  AS PopisKnihy,
                  COALESCE(d.KNIHY_CESTA,'')  AS KnihyCesta
                FROM KNIHY k
                LEFT JOIN AUTORY a ON k.AUTOR_ID = a.ID
                LEFT JOIN NAKLADATELSTVI n ON k.NAKLADATELSTVI_ID = n.ID
                LEFT JOIN KNIHY_DETAILY d ON k.ID = d.KNIHYDET_ID
                WHERE k.ID = @id";

            using var conn = new FbConnection(connString);
            conn.Open();
            using var cmd = new FbCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

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

        // cteni detailu knihy
        public static async Task<BookDetails?> GetBookDetailsAsync(string connString, int id)
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
                    END AS NAKLADATELSTVI,
                      COALESCE(d.POPIS_KNIHY,'')  AS PopisKnihy,
                      COALESCE(d.KNIHY_CESTA,'')  AS KnihyCesta
                    FROM KNIHY k
                    LEFT JOIN AUTORY a ON k.AUTOR_ID = a.ID
                    LEFT JOIN NAKLADATELSTVI n ON k.NAKLADATELSTVI_ID = n.ID
                    LEFT JOIN KNIHY_DETAILY d ON k.ID = d.KNIHYDET_ID
                    WHERE k.ID = @id";

            using var conn = new FbConnection(connString);
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

        // cteni seznamu knih (pro hlavni grid)
        public static async Task<List<BookRow>> GetBooksAsync(string connString)
        {
            const string sql = @"
                        SELECT
                          k.ID                                 AS ID,
                          k.NAZEV                              AS Nazev,
                          k.ROK                                AS Rok,
                          TRIM(COALESCE(a.PRIJMENI,'') || ' ' || COALESCE(a.JMENO,'')) AS Autor,
                          CASE
                          WHEN TRIM(COALESCE(n.NAZEV_FIRMY, '')) <> '' AND TRIM(COALESCE(n.MESTO, '')) <> ''
                               THEN TRIM(COALESCE(n.NAZEV_FIRMY, '')) || ', ' || TRIM(COALESCE(n.MESTO, ''))
                          WHEN TRIM(COALESCE(n.NAZEV_FIRMY, '')) <> ''
                               THEN TRIM(COALESCE(n.NAZEV_FIRMY, ''))
                          WHEN TRIM(COALESCE(n.MESTO, '')) <> ''
                               THEN TRIM(COALESCE(n.MESTO, ''))
                          ELSE ''
                        END AS NAKLADATELSTVI,
                          k.POCET_STRAN                        AS PocetStran
                        FROM KNIHY k
                        LEFT JOIN AUTORY a ON k.AUTOR_ID = a.ID
                        LEFT JOIN NAKLADATELSTVI n ON k.NAKLADATELSTVI_ID = n.ID
                        ORDER BY k.ID";

            var list = new List<BookRow>();
            using var conn = new FbConnection(connString);
            await conn.OpenAsync().ConfigureAwait(false);
            using var cmd = new FbCommand(sql, conn);
            using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await r.ReadAsync().ConfigureAwait(false))
            {
                list.Add(new BookRow
                {
                    ID = Convert.ToInt32(r["ID"]),
                    Nazev = Convert.ToString(r["Nazev"]) ?? "",
                    Rok = r["Rok"] == DBNull.Value ? null : Convert.ToInt32(r["Rok"]),
                    Autor = Convert.ToString(r["Autor"]) ?? "",
                    Nakladatelstvi = Convert.ToString(r["Nakladatelstvi"]) ?? "",
                    PocetStran = r["PocetStran"] == DBNull.Value ? null : Convert.ToInt32(r["PocetStran"])
                });
            }
            return list;
        }

        // ulozeni / uprava knihy (hlavni servis)
        public static async Task<int> SaveOrUpdateBookAsync(
            string connString,
            int? knihyID,
            string nazev,
            string? autorJmeno,
            string? autorPrijmeni,
            string? nakladNazev,
            string? nakladMesto,
            int? rok,
            int? pocetStran,
            string? popis,
            string? selectedFilePath,
            Func<string, int, string> storeCoverFile,
            Func<string?, string?> toAbsolutePath,
            string? zanryCsv)
        {
            using var conn = new FbConnection(connString);
            await conn.OpenAsync().ConfigureAwait(false);
            using var tx = conn.BeginTransaction();

            int autorId = await GetOrCreateAuthorAsync(conn, tx, autorJmeno, autorPrijmeni).ConfigureAwait(false);

            int? nakladId = null;
            if (!string.IsNullOrWhiteSpace(nakladNazev) || !string.IsNullOrWhiteSpace(nakladMesto))
                nakladId = await GetOrCreateNakladAsync(conn, tx, nakladNazev, nakladMesto).ConfigureAwait(false);

            int bookId = knihyID ?? await Db.ScalarAsync<int>(conn, tx, "SELECT COALESCE(MAX(ID),0)+1 FROM KNIHY").ConfigureAwait(false);

            if (knihyID == null)
            {
                await Db.NonQueryAsync(conn, tx, @"
                    INSERT INTO KNIHY (ID, NAZEV, ROK, AUTOR_ID, NAKLADATELSTVI_ID, POCET_STRAN)
                    VALUES (@id, @nazev, @rok, @aid, @nid, @ps)",
                    ("id", bookId), ("nazev", nazev), ("rok", rok),
                    ("aid", autorId), ("nid", nakladId), ("ps", pocetStran)).ConfigureAwait(false);
            }
            else
            {
                await Db.NonQueryAsync(conn, tx, @"
                                        UPDATE KNIHY SET
                                          NAZEV=@nazev,
                                          ROK=@rok,
                                          POCET_STRAN=@ps
                                        WHERE ID=@id",
                    ("id", bookId), ("nazev", nazev), ("rok", rok), ("ps", pocetStran)).ConfigureAwait(false);
            }

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

            await UpsertDetailsAsync(conn, tx, bookId, popis, newRelPath).ConfigureAwait(false);
            await SetZanryAsync(conn, tx, bookId, zanryCsv).ConfigureAwait(false);

            if (knihyID.HasValue)
                await ZmenaAutorNakladASmazaniAsync(conn, tx, bookId, autorId, nakladId).ConfigureAwait(false);

            await tx.CommitAsync();

            if (!string.IsNullOrWhiteSpace(oldAbsToDelete) && File.Exists(oldAbsToDelete))
            { try { File.Delete(oldAbsToDelete); } catch { /* ignore */ } }

            return bookId;
        }

        public static string? ToAbsolute(string? dbPath)
        {
            //prevest relativni cestu na absolutni (pro praci se soubory)
            if (string.IsNullOrWhiteSpace(dbPath)) return null;
            return Path.IsPathRooted(dbPath)
                ? dbPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
        }

        //-----------privatni-----------

        // najit/vytvorit autora
        private static async Task<int> GetOrCreateAuthorAsync(FbConnection conn, FbTransaction tx, string? jmeno, string? prijmeni)
        {
            var jm = (jmeno ?? string.Empty).Trim();
            var pr = (prijmeni ?? string.Empty).Trim();

            var existing = await Db.ScalarAsync<int?>(conn, tx,
                "SELECT FIRST 1 ID FROM AUTORY WHERE UPPER(TRIM(PRIJMENI))=UPPER(@pr) AND UPPER(TRIM(JMENO))=UPPER(@jm)",
                ("pr", pr), ("jm", jm)).ConfigureAwait(false);
            if (existing.HasValue) return existing.Value;

            var newId = await Db.ScalarAsync<int>(conn, tx, "SELECT COALESCE(MAX(ID),0)+1 FROM AUTORY").ConfigureAwait(false);
            await Db.NonQueryAsync(conn, tx, "INSERT INTO AUTORY (ID, PRIJMENI, JMENO) VALUES (@id, @pr, @jm)",
                ("id", newId), ("pr", pr), ("jm", jm)).ConfigureAwait(false);
            return newId;
        }

        //najit/vytvorit nakladatelstvi
        private static async Task<int> GetOrCreateNakladAsync(FbConnection conn, FbTransaction tx, string? name, string? mesto)
        {
            var n = (name ?? string.Empty).Trim();
            var m = (mesto ?? string.Empty).Trim();

            var existing = await Db.ScalarAsync<int?>(conn, tx,
                "SELECT FIRST 1 ID FROM NAKLADATELSTVI WHERE UPPER(TRIM(NAZEV_FIRMY))=UPPER(@n) AND UPPER(TRIM(COALESCE(MESTO,'')))=UPPER(@m)",
                ("n", n), ("m", m)).ConfigureAwait(false);
            if (existing.HasValue) return existing.Value;

            var newId = await Db.ScalarAsync<int>(conn, tx, "SELECT COALESCE(MAX(ID),0)+1 FROM NAKLADATELSTVI").ConfigureAwait(false);
            await Db.NonQueryAsync(conn, tx, "INSERT INTO NAKLADATELSTVI (ID, NAZEV_FIRMY, MESTO) VALUES (@id, @n, @m)",
                ("id", newId), ("n", n), ("m", m)).ConfigureAwait(false);
            return newId;
        }

        //nastaveni zanru
        private static async Task SetZanryAsync(FbConnection conn, FbTransaction tx, int bookId, string? csv)
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

                int gid;
                if (existing.HasValue)
                {
                    gid = existing.Value;
                }
                else
                {
                    gid = await Db.ScalarAsync<int>(conn, tx, "SELECT COALESCE(MAX(ZANRY_ID),0)+1 FROM ZANRY").ConfigureAwait(false);
                    await Db.NonQueryAsync(conn, tx, "INSERT INTO ZANRY (ZANRY_ID, NAZEV_ZANRY) VALUES (@id, @n)",
                        ("id", gid), ("n", name)).ConfigureAwait(false);
                }

                await Db.NonQueryAsync(conn, tx,
                    "INSERT INTO KNIHY_ZANRY (KNIHY_ID, ZANRY_ID) VALUES (@bid, @gid)",
                    ("bid", bookId), ("gid", gid)).ConfigureAwait(false);
            }
        }

        //upsert detailu
        private static async Task UpsertDetailsAsync(FbConnection conn, FbTransaction tx, int bookId, string? popis, string? relativePath)
        {
            var updated = await Db.NonQueryAsync(conn, tx,
                "UPDATE KNIHY_DETAILY SET POPIS_KNIHY=@p, KNIHY_CESTA=@cesta WHERE KNIHYDET_ID=@id",
                ("p", popis), ("cesta", relativePath), ("id", bookId)).ConfigureAwait(false);

            if (updated == 0)
            {
                await Db.NonQueryAsync(conn, tx,
                    "INSERT INTO KNIHY_DETAILY (KNIHYDET_ID, POPIS_KNIHY, KNIHY_CESTA) VALUES (@id, @p, @cesta)",
                    ("id", bookId), ("p", popis), ("cesta", relativePath)).ConfigureAwait(false);
            }
        }

        //zmena autora/nakladatele a prip. smazani orphanu
        private static async Task ZmenaAutorNakladASmazaniAsync(FbConnection conn, FbTransaction tx, int bookId, int autorId, int? nakladId)
        {
            int? oldAutor = await Db.ScalarAsync<int?>(conn, tx, "SELECT AUTOR_ID FROM KNIHY WHERE ID=@id", ("id", bookId)).ConfigureAwait(false);
            int? oldNaklad = await Db.ScalarAsync<int?>(conn, tx, "SELECT NAKLADATELSTVI_ID FROM KNIHY WHERE ID=@id", ("id", bookId)).ConfigureAwait(false);

            await Db.NonQueryAsync(conn, tx,
                "UPDATE KNIHY SET AUTOR_ID=@aid, NAKLADATELSTVI_ID=@nid WHERE ID=@id",
                ("aid", autorId), ("nid", nakladId), ("id", bookId)).ConfigureAwait(false);

            if (oldAutor.HasValue && oldAutor.Value != autorId)
            {
                var cnt = await Db.ScalarAsync<int>(conn, tx, "SELECT COUNT(*) FROM KNIHY WHERE AUTOR_ID=@aid", ("aid", oldAutor.Value)).ConfigureAwait(false);
                if (cnt == 0)
                    await Db.NonQueryAsync(conn, tx, "DELETE FROM AUTORY WHERE ID=@aid", ("aid", oldAutor.Value)).ConfigureAwait(false);
            }

            if (oldNaklad.HasValue && oldNaklad != nakladId)
            {
                var cnt = await Db.ScalarAsync<int>(conn, tx, "SELECT COUNT(*) FROM KNIHY WHERE NAKLADATELSTVI_ID=@nid", ("nid", oldNaklad.Value)).ConfigureAwait(false);
                if (cnt == 0)
                    await Db.NonQueryAsync(conn, tx, "DELETE FROM NAKLADATELSTVI WHERE ID=@nid", ("nid", oldNaklad.Value)).ConfigureAwait(false);
            }
        }

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

        internal static class Db
        {
            //vytvoreni FbCommand s parametry
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

            public static async Task<T?> ReadSingleAsync<T>(FbConnection conn, FbTransaction? tx, string sql,
                                                                        Func<IDataRecord, T> map,
                                                                        params (string, object?)[] ps)
            {
                using var cmd = Cmd(conn, tx, sql, ps);
                using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false);
                return await r.ReadAsync().ConfigureAwait(false) ? map(r) : default;
            }

            //ExecuteScalar vraci default pro null/DBNull
            public static T? Scalar<T>(FbConnection conn, FbTransaction? tx, string sql,
                                       params (string, object?)[] ps)
            {
                using var cmd = Cmd(conn, tx, sql, ps);
                var o = cmd.ExecuteScalar();
                if (o == null || o == DBNull.Value) return default;
                var t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                return (T)Convert.ChangeType(o, t);
            }

            //--- ASYNC HELPERY ---
            public static async Task<T?> ScalarAsync<T>(FbConnection conn, FbTransaction? tx, string sql,
                                                        params (string, object?)[] ps)
            {
                using var cmd = Cmd(conn, tx, sql, ps);
                var o = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                if (o == null || o == DBNull.Value) return default;
                var t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                return (T)Convert.ChangeType(o, t);
            }

            //prevest relativni cestu na absolutni
            public static string? ToAbsolute(string? dbPath)
            {
                if (string.IsNullOrWhiteSpace(dbPath))
                    return null;

                if (Path.IsPathRooted(dbPath))
                    return dbPath;

                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
            }

            //bezpecne smazani souboru
            public static void TryDeleteFile(string? absPath)
            {
                if (!string.IsNullOrWhiteSpace(absPath) && File.Exists(absPath))
                { try { File.Delete(absPath); } catch { /* ignore */ } }
            }
        }
    }
}