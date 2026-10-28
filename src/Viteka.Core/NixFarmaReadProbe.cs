using System.Data.Common;

namespace Viteka.Core;

// A fixed health query, never SQL received from the interface. Future utilities must
// use the same read-only boundary with their own approved, parameterized queries.
public static class NixFarmaReadProbe
{
    public static void Execute(DbConnection connection)
    {
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            using var readOnly = connection.CreateCommand();
            readOnly.Transaction = transaction;
            readOnly.CommandTimeout = 15;
            readOnly.CommandText = "SET TRANSACTION READ ONLY";
            readOnly.ExecuteNonQuery();
            using var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandTimeout = 15;
            query.CommandText = "SELECT 1 FROM DUAL";
            var result = query.ExecuteScalar();
            try
            {
                if (result is null or DBNull || Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) != 1)
                    throw new InvalidDataException("Unexpected probe result.");
            }
            catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException)
            { throw new InvalidDataException("Unexpected probe result.", error); }
        }
        finally
        {
            // Do not let a failed rollback hide the original Oracle error.
            try { transaction.Rollback(); } catch (DbException) { }
        }
    }
}
