using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Sample.EchoBot;
using Microsoft.Extensions.Configuration;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Internals;

namespace Microsoft.Bot.Sample.AspNetCore.Echo.Controllers
{
    [Route("api/[controller]")]
    public class MessagesController : Controller
    {
        private readonly IConfigurationRoot configuration;

        public MessagesController(IConfigurationRoot configuration)
        {
            this.configuration = configuration;
        }

        [Authorize(Roles = "Bot")]
        // POST api/values
        [HttpPost]
        public virtual async Task<OkResult> Post([FromBody]Activity activity)
        {
            if (activity == null) goto FINAL;
            // one of these will have an interface and process it
            switch (activity.GetActivityType())
            {
                case ActivityTypes.Message:
                    //await Conversation.SendAsync(activity, () => new EchoDialog());
                    //await Conversation.SendAsync(activity, () => EchoCommandDialog.dialog);
                    //await Conversation.SendAsync(activity, () => new EchoAttachmentDialog());
                    await Conversation.SendAsync(activity, () => EchoChainDialog.dialog);
                    break;

                case ActivityTypes.ConversationUpdate:
                    IConversationUpdateActivity update = activity;
                    using (var scope = DialogModule.BeginLifetimeScope(Conversation.Container, activity))
                    {
                        var client = scope.Resolve<IConnectorClient>();
                        if (update.MembersAdded.Any())
                        {
                            var reply = activity.CreateReply();
                            foreach (var newMember in update.MembersAdded)
                            {
                                if (newMember.Id != activity.Recipient.Id)
                                {
                                    reply.Text = $"Welcome {newMember.Name}!";
                                }
                                else
                                {
                                    reply.Text = $"Welcome {activity.From.Name}";
                                }
                                await client.Conversations.ReplyToActivityAsync(reply);
                            }
                        }
                    }
                    break;
                case ActivityTypes.ContactRelationUpdate:
                case ActivityTypes.Typing:
                case ActivityTypes.DeleteUserData:
                case ActivityTypes.Ping:
                default:
                    Trace.TraceError($"Unknown activity type ignored: {activity.GetActivityType()}");
                    break;
            }
            FINAL:
            return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted);
        }
    }
}
