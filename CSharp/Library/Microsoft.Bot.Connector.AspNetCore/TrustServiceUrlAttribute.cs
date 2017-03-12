using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace Microsoft.Bot.Connector
{
    public class TrustServiceUrlAttribute : ActionFilterAttribute
    {
        /// <summary>
        /// Whether to trust localhost and pass JwtToken to it.
        /// </summary>
        /// <remarks>Defaults to <c>false</c>.
        /// If you are using BotFramrworkEmulator with empty app ID &amp; password,
        /// it's strongly recommended that you set this property to <c>false</c> to bypass
        /// the authentication on MS server.</remarks>
        public static bool AutoTrustLocalhost { get; set; }

        public async override Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var activities = GetActivities(context);
            foreach (var activity in activities)
            {
                // TODO use some bulletproof predicate...
                if (!AutoTrustLocalhost &&
                    (activity.ServiceUrl.StartsWith("http://localhost") ||
                     activity.ServiceUrl.StartsWith("http://127.0.0.1")))
                    continue;
                    MicrosoftAppCredentials.TrustServiceUrl(activity.ServiceUrl);
            }
            await next();
        }

        public static IList<Activity> GetActivities(ActionExecutingContext actionContext)
        {
            var activties = actionContext.ActionArguments.Select(t => t.Value).OfType<Activity>().ToList();
            if (activties.Any())
            {
                return activties;
            }
            else
            {
                var objects =
                    actionContext.ActionArguments.Where(t => t.Value is JObject || t.Value is JArray)
                        .Select(t => t.Value).ToArray();
                if (objects.Any())
                {
                    activties = new List<Activity>();
                    foreach (var obj in objects)
                    {
                        activties.AddRange((obj is JObject) ? new Activity[] { ((JObject)obj).ToObject<Activity>() } : ((JArray)obj).ToObject<Activity[]>());
                    }
                }
            }
            return activties;
        }

    }
}
