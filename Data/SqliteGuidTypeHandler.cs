using Dapper;
using System.Data;

namespace RadiopaediaConnect.Data
{
    public class SqliteGuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value)
        {
            if (value is Guid g) return g;
            if (value is byte[] b) return new Guid(b);
            if (value is string s && Guid.TryParse(s, out var result))
            {
                return result;
            }
            return Guid.Empty;
        }

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.Value = value.ToString();
            parameter.DbType = DbType.String;
        }
    }
}