﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.FormFlow;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;

namespace Microsoft.Bot.Sample.AspNetCore.PizzaBot
{
    [LuisModel("4311ccf1-5ed1-44fe-9f10-a6adbad05c14", "6d0966209c6e4f6b835ce34492f3e6d9", LuisApiVersion.V2)]
    [Serializable]
    class PizzaOrderDialog : LuisDialog<PizzaOrder>
    {
        private readonly BuildFormDelegate<PizzaOrder> MakePizzaForm;

        internal PizzaOrderDialog(BuildFormDelegate<PizzaOrder> makePizzaForm)
        {
            this.MakePizzaForm = makePizzaForm;
        }

        [LuisIntent("")]
        public async Task None(IDialogContext context, LuisResult result)
        {
            await context.PostAsync("I'm sorry. I didn't understand you.");
            context.Wait(MessageReceived);
        }

        [LuisIntent("OrderPizza")]
        [LuisIntent("UseCoupon")]
        public async Task ProcessPizzaForm(IDialogContext context, LuisResult result)
        {
            var entities = new List<EntityRecommendation>(result.Entities);
            if (!entities.Any((entity) => entity.Type == "Kind"))
            {
                // Infer kind
                foreach (var entity in result.Entities)
                {
                    string kind = null;
                    switch (entity.Type)
                    {
                        case "Signature": kind = "Signature"; break;
                        case "GourmetDelite": kind = "Gourmet delite"; break;
                        case "Stuffed": kind = "stuffed"; break;
                        default:
                            if (entity.Type.StartsWith("BYO")) kind = "byo";
                            break;
                    }
                    if (kind != null)
                    {
                        entities.Add(new EntityRecommendation(type: "Kind") { Entity = kind });
                        break;
                    }
                }
            }

            var pizzaForm = new FormDialog<PizzaOrder>(new PizzaOrder(), this.MakePizzaForm, FormOptions.PromptInStart, entities);
            context.Call<PizzaOrder>(pizzaForm, PizzaFormComplete);
        }

        private async Task PizzaFormComplete(IDialogContext context, IAwaitable<PizzaOrder> result)
        {
            PizzaOrder order = null;
            try
            {
                order = await result;
            }
            catch (OperationCanceledException)
            {
                await context.PostAsync("You canceled the form!");
                return;
            }

            if (order != null)
            {
                await context.PostAsync("Your Pizza Order: " + order.ToString());
            }
            else
            {
                await context.PostAsync("Form returned empty response!");
            }

            context.Wait(MessageReceived);
        }

        enum Days { Saturday, Sunday, Monday, Tuesday, Wednesday, Thursday, Friday };

        [LuisIntent("StoreHours")]
        public async Task ProcessStoreHours(IDialogContext context, LuisResult result)
        {
            // Figuring out if the action is triggered or not
            var bestIntent = BestIntentFrom(result);
            var action = bestIntent.Actions.FirstOrDefault(t => t.Triggered.HasValue && t.Triggered.Value);
            if (action != null)
            {
                // extracting day parameter value from action parameters
                var dayParam = action.Parameters.Where(t => t.Name == "day").Select(t=> t.Value.FirstOrDefault(e => e.Type == "Day")?.Entity).First();
                Days day;
                if (Enum.TryParse(dayParam, true, out day))
                {
                    await this.StoreHoursResult(context, Awaitable.FromItem(day));
                    return;
                }
            }

            var days = (IEnumerable<Days>)Enum.GetValues(typeof(Days));
            PromptDialog.Choice(context, StoreHoursResult, days, "Which day of the week?",
                descriptions: from day in days
                              select (day == Days.Saturday || day == Days.Sunday) ? day.ToString() + "(no holidays)" : day.ToString());
        }

        private async Task StoreHoursResult(IDialogContext context, IAwaitable<Days> day)
        {
            var hours = string.Empty;
            switch (await day)
            {
                case Days.Saturday:
                case Days.Sunday:
                    hours = "5pm to 11pm";
                    break;
                default:
                    hours = "11am to 10pm";
                    break;
            }

            var text = $"Store hours are {hours}";
            await context.PostAsync(text);

            context.Wait(MessageReceived);
        }
    }
}