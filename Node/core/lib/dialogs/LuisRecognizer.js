"use strict";
var __extends = (this && this.__extends) || (function () {
    var extendStatics = Object.setPrototypeOf ||
        ({ __proto__: [] } instanceof Array && function (d, b) { d.__proto__ = b; }) ||
        function (d, b) { for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p]; };
    return function (d, b) {
        extendStatics(d, b);
        function __() { this.constructor = d; }
        d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
var IntentRecognizer_1 = require("./IntentRecognizer");
var request = require("request");
var url = require("url");
var LuisRecognizer = (function (_super) {
    __extends(LuisRecognizer, _super);
    function LuisRecognizer(models) {
        var _this = _super.call(this) || this;
        if (typeof models == 'string') {
            _this.models = { '*': models };
        }
        else {
            _this.models = (models || {});
        }
        return _this;
    }
    LuisRecognizer.prototype.onRecognize = function (context, callback) {
        var result = { score: 0.0, intent: null };
        if (context && context.message && context.message.text) {
            var utterance = context.message.text;
            var locale = context.locale || '*';
            var model = this.models.hasOwnProperty(locale) ? this.models[locale] : this.models['*'];
            if (model) {
                LuisRecognizer.recognize(utterance, model, function (err, intents, entities) {
                    if (!err) {
                        result.intents = intents;
                        result.entities = entities;
                        var top;
                        intents.forEach(function (intent) {
                            if (top) {
                                if (intent.score > top.score) {
                                    top = intent;
                                }
                            }
                            else {
                                top = intent;
                            }
                        });
                        if (top) {
                            result.score = top.score;
                            result.intent = top.intent;
                            switch (top.intent.toLowerCase()) {
                                case 'builtin.intent.none':
                                case 'none':
                                    result.score = 0.1;
                                    break;
                            }
                        }
                        callback(null, result);
                    }
                    else {
                        callback(err, null);
                    }
                });
            }
            else {
                callback(new Error("LUIS model not found for locale '" + locale + "'."), null);
            }
        }
        else {
            callback(null, result);
        }
    };
    LuisRecognizer.recognize = function (utterance, modelUrl, callback) {
        try {
            var uri = url.parse(modelUrl, true);
            uri.query['q'] = utterance || '';
            if (uri.search) {
                delete uri.search;
            }
            request.get(url.format(uri), function (err, res, body) {
                var result;
                try {
                    if (!err) {
                        result = JSON.parse(body);
                        result.intents = result.intents || [];
                        result.entities = result.entities || [];
                        if (result.topScoringIntent && result.intents.length == 0) {
                            result.intents.push(result.topScoringIntent);
                        }
                        if (result.intents.length == 1 && typeof result.intents[0].score !== 'number') {
                            result.intents[0].score = 1.0;
                        }
                    }
                }
                catch (e) {
                    err = e;
                }
                try {
                    if (!err) {
                        callback(null, result.intents, result.entities);
                    }
                    else {
                        var m = err.toString();
                        callback(err instanceof Error ? err : new Error(m));
                    }
                }
                catch (e) {
                    console.error(e.toString());
                }
            });
        }
        catch (err) {
            callback(err instanceof Error ? err : new Error(err.toString()));
        }
    };
    return LuisRecognizer;
}(IntentRecognizer_1.IntentRecognizer));
exports.LuisRecognizer = LuisRecognizer;
