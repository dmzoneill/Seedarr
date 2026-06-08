using System.Text.Json.Serialization;

namespace Seedarr.Http.REST;

public abstract class RestResource
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Id { get; set; }

    [JsonIgnore]
    public virtual string ResourceName
    {
        get
        {
            var typeName = GetType().Name;

            if (typeName.EndsWith("Resource"))
            {
                typeName = typeName[..^8];
            }

            return typeName.ToLower();
        }
    }
}
