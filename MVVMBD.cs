//MVVMBD.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using FirebirdSql.Data.FirebirdClient;

namespace ukol1
{
    public static class MvvmBD
    {
        //verejne API

        public static void DeleteBookCascade(string connString, int knihyID, Func<string?, string?> toAbsolutePath)
        {
            using var conn = new FbConnection(connString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            //cteni referenci a cesty obalky PRED mazanim
            int? autorID = Db.Scalar<int?>(conn, tx,
                "SELECT AUTOR_ID FROM KNIHY WHERE ID=@id", ("id", knihyID));
            int? nakladID = Db.Scalar<int?>(conn, tx,
                "SELECT NAKLADATELSTVI_ID FROM KNIHY WHERE ID=@id", ("id", knihyID));
            string? coverPath = Db.Scalar<string?>(conn, tx,
                "SELECT KNIHY_CESTA FROM KNIHY_DETAILY WHERE KNIHYDET_ID=@id", ("id", knihyID));

            //mazani zavislosti a samotne knihy
            Db.NonQuery(conn, tx, "DELETE FROM KNIHY_ZANRY   WHERE KNIHY_ID=@id", ("id", knihyID));
            Db.NonQuery(conn, tx, "DELETE FROM KNIHY_DETAILY WHERE KNIHYDET_ID=@id", ("id", knihyID));
            Db.NonQuery(conn, tx, "DELETE FROM KNIHY         WHERE ID=@id", ("id", knihyID));

            tx.Commit();

            //po commitu smazat soubor
            var abs = toAbsolutePath?.Invoke(coverPath) ?? Db.ToAbsolute(coverPath);
            Db.TryDeleteFile(abs);

            //uklid sirotku (autor/naklad)
            if (autorID.HasValue)
            {
                using var c = new FbConnection(connString);
                c.Open();
                if (Db.Scalar<int>(c, null, "SELECT COUNT(*) FROM KNIHY WHERE AUTOR_ID=@aid", ("aid", autorID.Value)) == 0)
                    Db.NonQuery(c, null, "DELETE FROM AUTORY WHERE ID=@aid", ("aid", autorID.Value));
            }
            if (nakladID.HasValue)
            {
                using var c = new FbConnection(connString);
                c.Open();
                if (Db.Scalar<int>(c, null, "SELECT COUNT(*) FROM KNIHY WHERE NAKLADATELSTVI_ID=@nid", ("nid", nakladID.Value)) == 0)
                    Db.NonQuery(c, null, "DELETE FROM NAKLADATELSTVI WHERE ID=@nid", ("nid", nakladID.Value));
            }
        }

        public static void DeleteBookCascade(string connString, int knihyID)
            => DeleteBookCascade(connString, knihyID, ToAbsolute);

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

        public static List<BookRow> GetBooks(string connString)
        {
            const string sql = @"
                SELECT
                  k.ID                                 AS ID,
                  k.NAZEV                              AS Nazev,
                  k.ROK                                AS Rok,
                  TRIM(COALESCE(a.PRIJMENI,'') || ' ' || COALESCE(a.JMENO,'')) AS Autor,
                  TRIM(COALESCE(n.NAZEV_FIRMY,'')) || ', ' || TRIM(COALESCE(n.MESTO,'')) AS Nakladatelstvi,
                  k.POCET_STRAN                        AS PocetStran
                FROM KNIHY k
                LEFT JOIN AUTORY a ON k.AUTOR_ID = a.ID
                LEFT JOIN NAKLADATELSTVI n ON k.NAKLADATELSTVI_ID = n.ID
                ORDER BY k.ID";

            var list = new List<BookRow>();
            using var conn = new FbConnection(connString);
            conn.Open();
            using var cmd = new FbCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
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

        //hlavni sluzba ukladani knihy
        public static int SaveOrUpdateBook(
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
            string? zanryCsv
        )
        {
            using var conn = new FbConnection(connString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            //Autor / Nakladatelstvi
            int autorId = GetOrCreateAuthor(conn, tx, autorJmeno, autorPrijmeni);

            int? nakladId = null;
            bool hasNaklad = !string.IsNullOrWhiteSpace(nakladNazev) || !string.IsNullOrWhiteSpace(nakladMesto);
            if (hasNaklad)
                nakladId = GetOrCreateNaklad(conn, tx, nakladNazev, nakladMesto);

            //KNIHY insert/update
            int bookId = knihyID ?? Db.Scalar<int>(conn, tx, "SELECT COALESCE(MAX(ID),0)+1 FROM KNIHY");

            if (knihyID == null)
            {
                Db.NonQuery(conn, tx, @"
                    INSERT INTO KNIHY (ID, NAZEV, ROK, AUTOR_ID, NAKLADATELSTVI_ID, POCET_STRAN)
                    VALUES (@id, @nazev, CAST(@rok AS INTEGER), @aid, CAST(@nid AS INTEGER), CAST(@ps AS INTEGER))",
                    ("id", bookId), ("nazev", nazev), ("rok", rok),
                    ("aid", autorId), ("nid", nakladId), ("ps", pocetStran));
            }
            else
            {
                Db.NonQuery(conn, tx, @"
                    UPDATE KNIHY SET
                      NAZEV = @nazev,
                      ROK   = CAST(@rok AS INTEGER),
                      POCET_STRAN = CAST(@ps AS INTEGER)
                    WHERE ID = @id",
                    ("id", bookId), ("nazev", nazev), ("rok", rok), ("ps", pocetStran));
            }

            //aktualni cesta obalky
            var oldRelPath = Db.Scalar<string?>(conn, tx,
                "SELECT KNIHY_CESTA FROM KNIHY_DETAILY WHERE KNIHYDET_ID=@id", ("id", bookId));

            //nova obalka (a pripadne co smazat)
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

            //detaily + zanry
            UpsertDetails(conn, tx, bookId, popis, newRelPath);
            SetZanry(conn, tx, bookId, zanryCsv);

            //pokud editace - zmenit autora/naklad a uklid sirotku
            if (knihyID.HasValue)
                ZmenaAutorNakladASmazani(conn, tx, bookId, autorId, nakladId);

            tx.Commit();

            //smazani stareho souboru po commitu
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

        //privatni

        //najit autora, pokud neexistuje - vytvorit
        private static int GetOrCreateAuthor(FbConnection conn, FbTransaction tx, string? jmeno, string? prijmeni)
        {
            var jm = (jmeno ?? string.Empty).Trim();
            var pr = (prijmeni ?? string.Empty).Trim();

            var existing = Db.Scalar<int?>(conn, tx,
                "SELECT FIRST 1 ID FROM AUTORY " +
                "WHERE UPPER(TRIM(PRIJMENI)) = UPPER(@pr) " +
                "  AND UPPER(TRIM(JMENO))    = UPPER(@jm)",
                ("pr", pr), ("jm", jm));
            if (existing.HasValue) return existing.Value;

            var newId = Db.Scalar<int>(conn, tx, "SELECT COALESCE(MAX(ID),0)+1 FROM AUTORY");
            Db.NonQuery(conn, tx, "INSERT INTO AUTORY (ID, PRIJMENI, JMENO) VALUES (@id, @pr, @jm)",
                ("id", newId), ("pr", pr), ("jm", jm));
            return newId;
        }

        //nakladatelstvi - najit/vytvorit
        private static int GetOrCreateNaklad(FbConnection conn, FbTransaction tx, string? name, string? mesto)
        {
            var n = (name ?? string.Empty).Trim();
            var m = (mesto ?? string.Empty).Trim();

            var existing = Db.Scalar<int?>(conn, tx,
                "SELECT FIRST 1 ID FROM NAKLADATELSTVI " +
                "WHERE UPPER(TRIM(NAZEV_FIRMY)) = UPPER(@n) " +
                "  AND UPPER(TRIM(COALESCE(MESTO,''))) = UPPER(@m)",
                ("n", n), ("m", m));
            if (existing.HasValue) return existing.Value;

            var newId = Db.Scalar<int>(conn, tx, "SELECT COALESCE(MAX(ID),0)+1 FROM NAKLADATELSTVI");
            Db.NonQuery(conn, tx, "INSERT INTO NAKLADATELSTVI (ID, NAZEV_FIRMY, MESTO) VALUES (@id, @n, @m)",
                ("id", newId), ("n", n), ("m", m));
            return newId;
        }

        //nastavit zanry knihy
        private static void SetZanry(FbConnection conn, FbTransaction tx, int bookId, string? csv)
        {
            Db.NonQuery(conn, tx, "DELETE FROM KNIHY_ZANRY WHERE KNIHY_ID=@id", ("id", bookId));
            if (string.IsNullOrWhiteSpace(csv)) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim();
                if (name.Length == 0) continue;
                if (!seen.Add(name)) continue;

                var existing = Db.Scalar<int?>(conn, tx,
                    "SELECT FIRST 1 ZANRY_ID FROM ZANRY WHERE UPPER(TRIM(NAZEV_ZANRY))=UPPER(@n)",
                    ("n", name));
                int gid;
                if (existing.HasValue)
                {
                    gid = existing.Value;
                }
                else
                {
                    gid = Db.Scalar<int>(conn, tx, "SELECT COALESCE(MAX(ZANRY_ID),0)+1 FROM ZANRY");
                    Db.NonQuery(conn, tx, "INSERT INTO ZANRY (ZANRY_ID, NAZEV_ZANRY) VALUES (@id, @n)",
                        ("id", gid), ("n", name));
                }

                Db.NonQuery(conn, tx,
                    "INSERT INTO KNIHY_ZANRY (KNIHY_ID, ZANRY_ID) VALUES (@bid, @gid)",
                    ("bid", bookId), ("gid", gid));
            }
        }

        //vlozeni/aktualizace detailu knihy
        private static void UpsertDetails(FbConnection conn, FbTransaction tx, int bookId, string? popis, string? relativePath)
        {
            var updated = Db.NonQuery(conn, tx,
                "UPDATE KNIHY_DETAILY SET POPIS_KNIHY=@p, KNIHY_CESTA=@cesta WHERE KNIHYDET_ID=@id",
                ("p", popis), ("cesta", relativePath), ("id", bookId));

            if (updated == 0)
            {
                Db.NonQuery(conn, tx,
                    "INSERT INTO KNIHY_DETAILY (KNIHYDET_ID, POPIS_KNIHY, KNIHY_CESTA) VALUES (@id, @p, @cesta)",
                    ("id", bookId), ("p", popis), ("cesta", relativePath));
            }
        }

        //zmena autora/nakladatele pri editaci + uklid sirotku
        private static void ZmenaAutorNakladASmazani(FbConnection conn, FbTransaction tx, int bookId, int autorId, int? nakladId)
        {
            int? oldAutor = Db.Scalar<int?>(conn, tx, "SELECT AUTOR_ID FROM KNIHY WHERE ID=@id", ("id", bookId));
            int? oldNaklad = Db.Scalar<int?>(conn, tx, "SELECT NAKLADATELSTVI_ID FROM KNIHY WHERE ID=@id", ("id", bookId));

            Db.NonQuery(conn, tx,
                "UPDATE KNIHY SET AUTOR_ID=@aid, NAKLADATELSTVI_ID=@nid WHERE ID=@id",
                ("aid", autorId), ("nid", nakladId), ("id", bookId));

            if (oldAutor.HasValue && oldAutor.Value != autorId)
            {
                var cnt = Db.Scalar<int>(conn, tx, "SELECT COUNT(*) FROM KNIHY WHERE AUTOR_ID=@aid", ("aid", oldAutor.Value));
                if (cnt == 0)
                    Db.NonQuery(conn, tx, "DELETE FROM AUTORY WHERE ID=@aid", ("aid", oldAutor.Value));
            }

            if (oldNaklad.HasValue && oldNaklad != nakladId)
            {
                var cnt = Db.Scalar<int>(conn, tx, "SELECT COUNT(*) FROM KNIHY WHERE NAKLADATELSTVI_ID=@nid", ("nid", oldNaklad.Value));
                if (cnt == 0)
                    Db.NonQuery(conn, tx, "DELETE FROM NAKLADATELSTVI WHERE ID=@nid", ("nid", oldNaklad.Value));
            }
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