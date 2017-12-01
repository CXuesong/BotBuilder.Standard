/*-----------------------------------------------------------------------------
This Bot demonstrates how to use an IntentDialog with a LuisRecognizer to add 
natural language support to a bot. The example also shows how to use 
UniversalBot.send() to push notifications to a user.

For a complete walkthrough of creating this bot see the article below.

    http://docs.botframework.com/builder/node/guides/understanding-natural-language/

-----------------------------------------------------------------------------*/

var builder = require('../../core/');

// Bot Storage: Here we register the state storage for your bot. 
// Default store: volatile in-memory store - Only for prototyping!
// We provide adapters for Azure Table, CosmosDb, SQL Azure, or you can implement your own!
// For samples and documentation, see: https://github.com/Microsoft/BotBuilder-Azure
var inMemoryStorage = new builder.MemoryBotStorage();

// Create bot and bind to console
var connector = new builder.ConsoleConnector().listen();
var bot = new builder.UniversalBot(connector, function (session) {
    session.send("Hi... I'm the alarm bot sample. I can set new alarms or delete existing ones.");
}).set('storage', inMemoryStorage); // Register in memory storage

// Add global LUIS recognizer to bot
var model = process.env.model || 'https://api.projectoxford.ai/luis/v2.0/apps/c413b2ef-382c-45bd-8ff0-f76d60e2a821?subscription-key=6d0966209c6e4f6b835ce34492f3e6d9';
bot.recognizer(new builder.LuisRecognizer(model));

// Set Alarm dialog
bot.dialog('/setAlarm', [
    function (session, args, next) {
        // Resolve and store any entities passed from LUIS.
        var intent = args.intent;
        var title = builder.EntityRecognizer.findEntity(intent.entities, 'builtin.alarm.title');
        var time = builder.EntityRecognizer.resolveTime(intent.entities);
        var alarm = session.dialogData.alarm = {
          title: title ? title.entity : null,
          timestamp: time ? time.getTime() : null  
        };
        
        // Prompt for title
        if (!alarm.title) {
            builder.Prompts.text(session, 'What would you like to call your alarm?');
        } else {
            next();
        }
    },
    function (session, results, next) {
        var alarm = session.dialogData.alarm;
        if (results.response) {
            alarm.title = results.response;
        }

        // Prompt for time
        if (!alarm.timestamp) {
            builder.Prompts.time(session, 'What time would you like to set the alarm for?');
        } else {
            next();
        }
    },
    function (session, results) {
        var alarm = session.dialogData.alarm;
        if (results.response) {
            var time = builder.EntityRecognizer.resolveTime([results.response]);
            alarm.timestamp = time ? time.getTime() : null;
        }
        
        // Save address of who to notify and write to scheduler.
        alarm.address = session.message.address;
        alarms[alarm.title] = alarm;
        
        // Send confirmation to user
        var date = new Date(alarm.timestamp);
        var isAM = date.getHours() < 12;
        session.endDialog('Creating alarm named "%s" for %d/%d/%d %d:%02d%s',
            alarm.title,
            date.getMonth() + 1, date.getDate(), date.getFullYear(),
            isAM ? date.getHours() : date.getHours() - 12, date.getMinutes(), isAM ? 'am' : 'pm');
    }
]).triggerAction({ 
    matches: 'builtin.intent.alarm.set_alarm',
    confirmPrompt: "This will cancel the current alarm. Are you sure?"
}).cancelAction('cancelSetAlarm', "Alarm canceled.", {
    matches: /^(cancel|nevermind)/i,
    confirmPrompt: "Are you sure?"
});

// Delete Alarm dialog
bot.dialog('/deleteAlarm', [
    function (session, args, next) {
        if (alarmCount() > 0) {
            // Resolve entities passed from LUIS.
            var title;
            var intent = args.intent;
            var entity = builder.EntityRecognizer.findEntity(intent.entities, 'builtin.alarm.title');
            if (entity) {
                // Verify its in our set of alarms.
                title = builder.EntityRecognizer.findBestMatch(alarms, entity.entity);
            }
            
            // Prompt for alarm name
            if (!title) {
                builder.Prompts.choice(session, 'Which alarm would you like to delete?', alarms);
            } else {
                next({ response: title });
            }
        } else {
            session.endDialog("No alarms to delete.");
        }
    },
    function (session, results) {
        delete alarms[results.response.entity];
        session.endDialog("Deleted the '%s' alarm.", results.response.entity);
    }
]).triggerAction({
    matches: 'builtin.intent.alarm.delete_alarm'
}).cancelAction('cancelDeleteAlarm', "Ok.", {
    matches: /^(cancel|nevermind)/i
});

// Very simple alarm scheduler
var alarms = {};
setInterval(function () {
    var now = new Date().getTime();
    for (var key in alarms) {
        var alarm = alarms[key];
        if (now >= alarm.timestamp) {
            var msg = new builder.Message()
                .address(alarm.address)
                .text("Here's your '%s' alarm.", alarm.title);
            bot.send(msg);
            delete alarms[key];
        }
    }
}, 15000);

// Helpers
function alarmCount() {
    var i = 0;
    for (var name in alarms) {
        i++;
    }
    return i;
}
