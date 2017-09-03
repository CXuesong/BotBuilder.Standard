"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var consts = require("../consts");
var utils = require("../utils");
var async = require("async");
var ActionSet = (function () {
    function ActionSet() {
        this.actions = {};
    }
    ActionSet.prototype.clone = function (copyTo) {
        var obj = copyTo || new ActionSet();
        obj.trigger = this.trigger;
        for (var name in this.actions) {
            obj.actions[name] = this.actions[name];
        }
        return obj;
    };
    ActionSet.prototype.addDialogTrigger = function (actions, dialogId) {
        if (this.trigger) {
            this.trigger.localizationNamespace = dialogId.split(':')[0];
            actions.beginDialogAction(dialogId, dialogId, this.trigger);
        }
    };
    ActionSet.prototype.findActionRoutes = function (context, callback) {
        var results = [{ score: 0.0, libraryName: context.libraryName }];
        function addRoute(route) {
            if (route.score > 0 && route.routeData) {
                route.routeData.libraryName = context.libraryName;
                if (route.score > results[0].score) {
                    results = [route];
                }
                else if (route.score == results[0].score) {
                    results.push(route);
                }
            }
        }
        function matchExpression(action, entry, cb) {
            if (entry.options.matches) {
                var bestScore = 0.0;
                var routeData;
                var matches = Array.isArray(entry.options.matches) ? entry.options.matches : [entry.options.matches];
                matches.forEach(function (exp) {
                    if (typeof exp == 'string') {
                        if (context.intent && exp === context.intent.intent && context.intent.score > bestScore) {
                            bestScore = context.intent.score;
                            routeData = {
                                action: action,
                                intent: context.intent
                            };
                        }
                    }
                    else {
                        var matches = exp.exec(text);
                        if (matches && matches.length) {
                            var intent = {
                                score: 0.4 + ((matches[0].length / text.length) * 0.6),
                                intent: exp.toString(),
                                expression: exp,
                                matched: matches
                            };
                            if (intent.score > bestScore) {
                                bestScore = intent.score;
                                routeData = {
                                    action: action,
                                    intent: intent
                                };
                            }
                        }
                    }
                });
                var intentThreshold = entry.options.intentThreshold || 0.1;
                if (bestScore >= intentThreshold) {
                    cb(null, bestScore, routeData);
                }
                else {
                    cb(null, 0.0, null);
                }
            }
            else {
                cb(null, 0.0, null);
            }
        }
        var text = context.message.text || '';
        if (text.indexOf('action?') == 0) {
            var parts = text.split('?')[1].split('=');
            var name = parts[0];
            if (this.actions.hasOwnProperty(name)) {
                var options = this.actions[name].options;
                var routeData = { action: name };
                if (parts.length > 1) {
                    parts.shift();
                    routeData.data = parts.join('=');
                }
                addRoute({
                    score: 1.0,
                    libraryName: context.libraryName,
                    routeType: context.routeType,
                    routeData: routeData
                });
            }
            callback(null, results);
        }
        else {
            async.forEachOf(this.actions, function (entry, action, cb) {
                if (entry.options.onFindAction) {
                    entry.options.onFindAction(context, function (err, score, routeData) {
                        if (!err) {
                            routeData = routeData || {};
                            routeData.action = action;
                            addRoute({
                                score: score,
                                libraryName: context.libraryName,
                                routeType: context.routeType,
                                routeData: routeData
                            });
                        }
                        cb(err);
                    });
                }
                else {
                    matchExpression(action, entry, function (err, score, routeData) {
                        if (!err && routeData) {
                            addRoute({
                                score: score,
                                libraryName: context.libraryName,
                                routeType: context.routeType,
                                routeData: routeData
                            });
                        }
                        cb(err);
                    });
                }
            }, function (err) {
                if (!err) {
                    callback(null, results);
                }
                else {
                    callback(err, null);
                }
            });
        }
    };
    ActionSet.prototype.selectActionRoute = function (session, route) {
        function next() {
            entry.handler(session, routeData);
        }
        var routeData = route.routeData;
        var entry = this.actions[routeData.action];
        if (entry.options.onSelectAction) {
            entry.options.onSelectAction(session, routeData, next);
        }
        else {
            next();
        }
    };
    ActionSet.prototype.dialogInterrupted = function (session, dialogId, dialogArgs) {
        var trigger = this.trigger;
        function next() {
            if (trigger && trigger.confirmPrompt) {
                session.beginDialog(consts.DialogId.ConfirmInterruption, {
                    dialogId: dialogId,
                    dialogArgs: dialogArgs,
                    confirmPrompt: trigger.confirmPrompt,
                    localizationNamespace: trigger.localizationNamespace
                });
            }
            else {
                session.clearDialogStack();
                session.beginDialog(dialogId, dialogArgs);
            }
        }
        if (trigger && trigger.onInterrupted) {
            this.trigger.onInterrupted(session, dialogId, dialogArgs, next);
        }
        else {
            next();
        }
    };
    ActionSet.prototype.cancelAction = function (name, msg, options) {
        return this.action(name, function (session, args) {
            if (options.confirmPrompt) {
                session.beginDialog(consts.DialogId.ConfirmCancel, {
                    localizationNamespace: args.libraryName,
                    confirmPrompt: options.confirmPrompt,
                    dialogIndex: args.dialogIndex,
                    message: msg
                });
            }
            else {
                if (msg) {
                    session.sendLocalized(args.libraryName, msg);
                }
                session.cancelDialog(args.dialogIndex);
            }
        }, options);
    };
    ActionSet.prototype.reloadAction = function (name, msg, options) {
        if (options === void 0) { options = {}; }
        return this.action(name, function (session, args) {
            if (msg) {
                session.sendLocalized(args.libraryName, msg);
            }
            session.cancelDialog(args.dialogIndex, args.dialogId, options.dialogArgs);
        }, options);
    };
    ActionSet.prototype.beginDialogAction = function (name, id, options) {
        if (options === void 0) { options = {}; }
        return this.action(name, function (session, args) {
            if (options.dialogArgs) {
                utils.copyTo(options.dialogArgs, args);
            }
            if (id.indexOf(':') < 0) {
                var lib = args.dialogId ? args.dialogId.split(':')[0] : args.libraryName;
                id = lib + ':' + id;
            }
            if (session.sessionState.callstack.length > 0) {
                if (options.isInterruption) {
                    var parts = session.sessionState.callstack[0].id.split(':');
                    var dialog = session.library.findDialog(parts[0], parts[1]);
                    dialog.dialogInterrupted(session, id, args);
                }
                else {
                    session.beginDialog(consts.DialogId.Interruption, { dialogId: id, dialogArgs: args });
                }
            }
            else {
                session.beginDialog(id, args);
            }
        }, options);
    };
    ActionSet.prototype.endConversationAction = function (name, msg, options) {
        return this.action(name, function (session, args) {
            if (options.confirmPrompt) {
                session.beginDialog(consts.DialogId.ConfirmCancel, {
                    localizationNamespace: args.libraryName,
                    confirmPrompt: options.confirmPrompt,
                    endConversation: true,
                    message: msg
                });
            }
            else {
                if (msg) {
                    session.sendLocalized(args.libraryName, msg);
                }
                session.endConversation();
            }
        }, options);
    };
    ActionSet.prototype.triggerAction = function (options) {
        this.trigger = (options || {});
        this.trigger.isInterruption = true;
        ;
        return this;
    };
    ActionSet.prototype.customAction = function (options) {
        if (!options || !options.onSelectAction) {
            throw "An onSelectAction handler is required.";
        }
        var name = options.matches ? 'custom(' + options.matches.toString() + ')' : 'custom(onFindAction())';
        return this.action(name, function (session, args) {
            session.logger.warn(session.dialogStack(), "Shouldn't call next() in onSelectAction() for " + name);
            session.save().sendBatch();
        }, options);
    };
    ActionSet.prototype.action = function (name, handler, options) {
        if (options === void 0) { options = {}; }
        var key = this.uniqueActionName(name);
        this.actions[name] = { handler: handler, options: options };
        return this;
    };
    ActionSet.prototype.uniqueActionName = function (name, cnt) {
        if (cnt === void 0) { cnt = 1; }
        var key = cnt > 1 ? name + cnt : name;
        if (this.actions.hasOwnProperty(key)) {
            return this.uniqueActionName(name, cnt + 1);
        }
        else {
            return key;
        }
    };
    return ActionSet;
}());
exports.ActionSet = ActionSet;
