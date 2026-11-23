using March7thHoney.Util;
using March7thHoney.WebServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private static void MapSettingsRoutes(IEndpointRouteBuilder api)
    {
        api.MapGet("/settings", GetSettings);
        api.MapPut("/settings", UpdateSettings);
    }

    private static IResult GetSettings()
    {
        return Success(new SettingsData(AdminSettingsRegistry.Snapshot()));
    }

    private static IResult UpdateSettings(AdminSettingsRequest? request)
    {
        request ??= new AdminSettingsRequest();
        if (request.values == null || request.values.Count == 0)
            return Error("Nothing to save");

        var result = AdminSettingsRegistry.Apply(request.values);
        return Success(new SettingsApplyData(
            result.Applied,
            result.Errors,
            result.RestartRequired,
            result.Persisted,
            AdminSettingsRegistry.Snapshot()));
    }

    public class AdminSettingsRequest
    {
        public Dictionary<string, string>? values { get; set; }
    }
}
