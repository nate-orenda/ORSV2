// Models/FunctionEndpointsOptions.cs
namespace ORSV2.Models
{
    public class FunctionEndpoint
    {
        public string? Url { get; set; }
        public string Method { get; set; } = "GET";
        public string DistrictQueryName { get; set; } = "district_id";
        public string? KeyHeaderName { get; set; } // usually null when using host key
        public string? KeyValue { get; set; }      // usually null when using host key
    }

    public class FunctionEndpointsOptions
    {
        public string? HostKeyHeaderName { get; set; } // "x-functions-key"
        public string? HostKeyValue { get; set; }      // your _master key
        public Dictionary<string, FunctionEndpoint> Endpoints { get; set; } = new();
    }
}
