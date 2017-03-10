using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Microsoft.Bot.Connector
{
    /// <summary>
    /// An Activity is the basic communication type for the Bot Framework 3.0 protocol
    /// </summary>
    /// <remarks>
    /// The Activity class contains all properties that individual, more specific activities
    /// could contain. It is a superset type.
    /// </remarks>
    public partial class Activity :
        IActivity,
        IConversationUpdateActivity,
        IContactRelationUpdateActivity,
        IMessageActivity,
        ITypingActivity,
        IEndOfConversationActivity,
        IEventActivity,
        IInvokeActivity
    {
        /// <summary>
        /// Content-type for an Activity
        /// </summary>
        public const string ContentType = "application/vnd.microsoft.activity";

        /// <summary>
        /// Take a message and create a reply message for it with the routing information 
        /// set up to correctly route a reply to the source message
        /// </summary>
        /// <param name="text">text you want to reply with</param>
        /// <param name="locale">language of your reply</param>
        /// <returns>message set up to route back to the sender</returns>
        public Activity CreateReply(string text = null, string locale = null)
        {
            Activity reply = new Activity();
            reply.Type = ActivityTypes.Message;
            reply.Timestamp = DateTime.UtcNow;
            reply.From = new ChannelAccount(id: this.Recipient.Id, name: this.Recipient.Name);
            reply.Recipient = new ChannelAccount(id: this.From.Id, name: this.From.Name);
            reply.ReplyToId = this.Id;
            reply.ServiceUrl = this.ServiceUrl;
            reply.ChannelId = this.ChannelId;
            reply.Conversation = new ConversationAccount(isGroup: this.Conversation.IsGroup, id: this.Conversation.Id, name: this.Conversation.Name);
            reply.Text = text ?? String.Empty;
            reply.Locale = locale ?? this.Locale;
            reply.Attachments = new List<Attachment>();
            reply.Entities = new List<Entity>();
            return reply;
        }

        /// <summary>
        /// Create an instance of the Activity class with IMessageActivity masking
        /// </summary>
        public static IMessageActivity CreateMessageActivity() { return new Activity(ActivityTypes.Message); }

        /// <summary>
        /// Create an instance of the Activity class with IContactRelationUpdateActivity masking
        /// </summary>
        public static IContactRelationUpdateActivity CreateContactRelationUpdateActivity() { return new Activity(ActivityTypes.ContactRelationUpdate); }

        /// <summary>
        /// Create an instance of the Activity class with IConversationUpdateActivity masking
        /// </summary>
        public static IConversationUpdateActivity CreateConversationUpdateActivity() { return new Activity(ActivityTypes.ConversationUpdate); }

        /// <summary>
        /// Create an instance of the Activity class with ITypingActivity masking
        /// </summary>
        public static ITypingActivity CreateTypingActivity() { return new Activity(ActivityTypes.Typing); }

        /// <summary>
        /// Create an instance of the Activity class with IActivity masking
        /// </summary>
        public static IActivity CreatePingActivity() { return new Activity(ActivityTypes.Ping); }

        /// <summary>
        /// Create an instance of the Activity class with IEndOfConversationActivity masking
        /// </summary>
        public static IEndOfConversationActivity CreateEndOfConversationActivity() { return new Activity(ActivityTypes.EndOfConversation); }

        /// <summary>
        /// Create an instance of the Activity class with an IEventActivity masking
        /// </summary>
        public static IEventActivity CreateEventActivity() { return new Activity(ActivityTypes.Event); }

        /// <summary>
        /// Create an instance of the Activity class with IInvokeActivity masking
        /// </summary>
        public static IInvokeActivity CreateInvokeActivity() { return new Activity(ActivityTypes.Invoke); }

        /// <summary>
        /// True if the Activity is of the specified activity type
        /// </summary>
        protected bool IsActivity(string activity) { return string.Compare(this.Type?.Split('/').First(), activity, true) == 0; }

        /// <summary>
        /// Return an IMessageActivity mask if this is a message activity
        /// </summary>
        public IMessageActivity AsMessageActivity() { return IsActivity(ActivityTypes.Message) ? this : null; }

        /// <summary>
        /// Return an IContactRelationUpdateActivity mask if this is a contact relation update activity
        /// </summary>
        public IContactRelationUpdateActivity AsContactRelationUpdateActivity() { return IsActivity(ActivityTypes.ContactRelationUpdate) ? this : null; }

        /// <summary>
        /// Return an IConversationUpdateActivity mask if this is a conversation update activity
        /// </summary>
        public IConversationUpdateActivity AsConversationUpdateActivity() { return IsActivity(ActivityTypes.ConversationUpdate) ? this : null; }

        /// <summary>
        /// Return an ITypingActivity mask if this is a typing activity
        /// </summary>
        public ITypingActivity AsTypingActivity() { return IsActivity(ActivityTypes.Typing) ? this : null; }

        /// <summary>
        /// Return an IEndOfConversationActivity mask if this is an end of conversation activity
        /// </summary>
        public IEndOfConversationActivity AsEndOfConversationActivity() { return IsActivity(ActivityTypes.EndOfConversation) ? this : null; }

        /// <summary>
        /// Return an IEventActivity mask if this is an event activity
        /// </summary>
        public IEventActivity AsEventActivity() { return IsActivity(ActivityTypes.Event) ? this : null; }

        /// <summary>
        /// Return an IInvokeActivity mask if this is an invoke activity
        /// </summary>
        public IInvokeActivity AsInvokeActivity() { return IsActivity(ActivityTypes.Invoke) ? this : null; }

        /// <summary>
        /// Get StateClient appropriate for this activity
        /// </summary>
        /// <param name="credentials">credentials for bot to access state api</param>
        /// <param name="serviceUrl">alternate serviceurl to use for state service</param>
        /// <param name="handlers"></param>
        /// <returns></returns>
        public StateClient GetStateClient(MicrosoftAppCredentials credentials, string serviceUrl = null, params DelegatingHandler[] handlers)
        {
            bool useServiceUrl = (this.ChannelId == "emulator");
            if (useServiceUrl)
                return new StateClient(new Uri(this.ServiceUrl), credentials: credentials, handlers: handlers);

            if (serviceUrl != null)
                return new StateClient(new Uri(serviceUrl), credentials: credentials, handlers: handlers);

            return new StateClient(credentials, true, handlers);
        }

        /// <summary>
        /// Get StateClient appropriate for this activity
        /// </summary>
        /// <param name="microsoftAppId"></param>
        /// <param name="microsoftAppPassword"></param>
        /// <param name="serviceUrl">alternate serviceurl to use for state service</param>
        /// <param name="handlers"></param>
        /// <returns></returns>
        public StateClient GetStateClient(string microsoftAppId = null, string microsoftAppPassword = null, string serviceUrl = null, params DelegatingHandler[] handlers)
        {
            return GetStateClient(new MicrosoftAppCredentials(microsoftAppId, microsoftAppPassword), serviceUrl, handlers);
        }

        /// <summary>
        /// Check if the message has content
        /// </summary>
        /// <returns>Returns true if this message has any content to send</returns>
        public bool HasContent()
        {
            if (!String.IsNullOrWhiteSpace(this.Text))
                return true;

            if (!String.IsNullOrWhiteSpace(this.Summary))
                return true;

            if (this.Attachments != null && this.Attachments.Any())
                return true;

            if (this.ChannelData != null)
                return true;

            return false;
        }

        /// <summary>
        /// Get mentions 
        /// </summary>
        /// <returns></returns>
        public Mention[] GetMentions()
        {
            return this.Entities?.Where(entity => String.Compare(entity.Type, "mention", ignoreCase: true) == 0).Select(e => e.Properties.ToObject<Mention>()).ToArray() ?? new Mention[0];
        }

        /// <summary>
        /// Is there a mention of Id in the Text Property 
        /// </summary>
        /// <param name="id">ChannelAccount.Id</param>
        /// <returns>true if this id is mentioned in the text</returns>
        public bool MentionsId(string id)
        {
            return this.GetMentions().Where(mention => mention.Mentioned.Id == id).Any();
        }

        /// <summary>
        /// Is there a mention of Recipient.Id in the Text Property 
        /// </summary>
        /// <returns>true if this id is mentioned in the text</returns>
        public bool MentionsRecipient()
        {
            return this.GetMentions().Where(mention => mention.Mentioned.Id == this.Recipient.Id).Any();
        }

        /// <summary>
        /// Remove recipient mention text from Text property
        /// </summary>
        /// <returns>new .Text property value</returns>
        public string RemoveRecipientMention()
        {
            return RemoveMentionText(this.Recipient.Id);
        }

        /// <summary>
        /// Replace any mention text for given id from Text property
        /// </summary>
        /// <param name="id">id to match</param>
        /// <returns>new .Text property value</returns>
        public string RemoveMentionText(string id)
        {
            foreach (var mention in this.GetMentions().Where(mention => mention.Mentioned.Id == id))
            {
                Text = Regex.Replace(Text, mention.Text, "", RegexOptions.IgnoreCase);
            }
            return this.Text;
        }

        /// <summary>
        /// Get channeldata as typed structure
        /// </summary>
        /// <typeparam name="TypeT">type to use</typeparam>
        /// <returns>typed object or default(TypeT)</returns>
        public TypeT GetChannelData<TypeT>()
        {
            if (this.ChannelData == null)
                return default(TypeT);
            return ((JObject)this.ChannelData).ToObject<TypeT>();
        }

        /// <summary>
        /// Return the "major" portion of the activity
        /// </summary>
        /// <returns>normalized major portion of the activity, aka message/... will return "message"</returns>
        public string GetActivityType()
        {
            var type = this.Type.Split('/').First();
            return GetActivityType(type);
        }

        public static string GetActivityType(string type)
        {
            if (String.Equals(type, ActivityTypes.Message, StringComparison.OrdinalIgnoreCase))
                return ActivityTypes.Message;

            if (String.Equals(type, ActivityTypes.ContactRelationUpdate, StringComparison.OrdinalIgnoreCase))
                return ActivityTypes.ContactRelationUpdate;

            if (String.Equals(type, ActivityTypes.ConversationUpdate, StringComparison.OrdinalIgnoreCase))
                return ActivityTypes.ConversationUpdate;

            if (String.Equals(type, ActivityTypes.DeleteUserData, StringComparison.OrdinalIgnoreCase))
                return ActivityTypes.DeleteUserData;

            if (String.Equals(type, ActivityTypes.Typing, StringComparison.OrdinalIgnoreCase))
                return ActivityTypes.Typing;

            if (String.Equals(type, ActivityTypes.Ping, StringComparison.OrdinalIgnoreCase))
                return ActivityTypes.Ping;

            return $"{Char.ToLower(type[0])}{type.Substring(1)}";
        }
    }
}
