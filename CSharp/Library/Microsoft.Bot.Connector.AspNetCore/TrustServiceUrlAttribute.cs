using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace Microsoft.Bot.Connector
{
    // TODO Documentation
    public class TrustServiceUrlAttribute : TypeFilterAttribute
    {
        /// <inheritdoc />
        public TrustServiceUrlAttribute() : base(typeof(TrustServiceUrlFilter))
        {
        }

        private sealed class TrustServiceUrlFilter : IAsyncActionFilter
        {
            private readonly bool canTrustServices;

            public TrustServiceUrlFilter(MicrosoftAppCredentials credentials)
            {
                canTrustServices = !string.IsNullOrEmpty(credentials.MicrosoftAppId);
            }

            /// <inheritdoc />
            public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                //          Per BotAuthenticator.TrustServiceUrls
                // add the service url to the list of trusted urls (only if the JwtToken
                // is valid) and identity is not null
                if (canTrustServices)
                {
                    var activities = GetActivities(context);
                    foreach (var activity in activities)
                    {
                        MicrosoftAppCredentials.TrustServiceUrl(activity.ServiceUrl);
                    }
                }
                return next();
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
}
