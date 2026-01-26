using Dapper;
using System.Data;
using System.Globalization;

namespace RadiopaediaConnect.Data
{
    public class SqliteDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        private const string Format = "yyyy-MM-dd HH:mm:ss.fffffff";

        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.Value = value.ToString(Format, CultureInfo.InvariantCulture);
        }

        public override DateTime Parse(object value)
        {
            if (value == null) return DateTime.MinValue;

            if (DateTime.TryParseExact((string)value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                return result;
            }
            return DateTime.Parse((string)value);
        }
    }
}