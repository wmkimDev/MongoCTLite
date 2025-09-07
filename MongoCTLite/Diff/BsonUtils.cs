using MongoDB.Bson;

namespace MongoCTLite.Diff;

public static class BsonUtils
{
    /// <summary>
    /// Checks if the BSON type is numeric
    /// </summary>
    public static bool IsNumeric(BsonType type)
    {
        return type == BsonType.Int32 || 
               type == BsonType.Int64 || 
               type == BsonType.Double ||
               type == BsonType.Decimal128;
    }
    
    /// <summary>
    /// Checks if both BSON values are numeric types
    /// </summary>
    public static bool AreBothNumeric(BsonValue a, BsonValue b)
    {
        return IsNumeric(a.BsonType) && IsNumeric(b.BsonType);
    }
    
    /// <summary>
    /// Safely converts a BSON value to long
    /// </summary>
    public static long ToInt64Safe(BsonValue value)
    {
        return value.BsonType switch
        {
            BsonType.Int32      => value.AsInt32,
            BsonType.Int64      => value.AsInt64,
            BsonType.Double     => (long)value.AsDouble,
            BsonType.Decimal128 => (long)value.AsDecimal,
            _                   => 0
        };
    }
    
    /// <summary>
    /// Compares two BSON values for equality
    /// </summary>
    public static bool Equals(BsonValue a, BsonValue b)
    {
        return a.Equals(b);
    }
}