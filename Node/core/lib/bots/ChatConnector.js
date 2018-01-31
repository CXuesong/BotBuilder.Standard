"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var OpenIdMetadata_1 = require("./OpenIdMetadata");
var utils = require("../utils");
var logger = require("../logger");
var consts = require("../consts");
var request = require("request");
var async = require("async");
var jwt = require("jsonwebtoken");
var zlib = require("zlib");
var Promise = require("promise");
var urlJoin = require("url-join");
var pjson = require('../../package.json');
var MAX_DATA_LENGTH = 65000;
var USER_AGENT = "Microsoft-BotFramework/3.1 (BotBuilder Node.js/" + pjson.version + ")";
var StateApiDreprecatedMessage = "The Bot State API is deprecated.  Please refer to https://aka.ms/I6swrh for details on how to replace with your own storage.";
var ChatConnector = (function () {
    function ChatConnector(settings) {
        if (settings === void 0) { settings = {}; }
        this.settings = settings;
        if (!this.settings.endpoint) {
            this.settings.endpoint = {
                refreshEndpoint: 'https://login.microsoftonline.com/botframework.com/oauth2/v2.0/token',
                refreshScope: 'https://api.botframework.com/.default',
                botConnectorOpenIdMetadata: this.settings.openIdMetadata || 'https://login.botframework.com/v1/.well-known/openidconfiguration',
                botConnectorIssuer: 'https://api.botframework.com',
                botConnectorAudience: this.settings.appId,
                emulatorOpenIdMetadata: 'https://login.microsoftonline.com/botframework.com/v2.0/.well-known/openid-configuration',
                emulatorAudience: this.settings.appId,
                emulatorAuthV31IssuerV1: 'https://sts.windows.net/d6d49420-f39b-4df7-a1dc-d59a935871db/',
                emulatorAuthV31IssuerV2: 'https://login.microsoftonline.com/d6d49420-f39b-4df7-a1dc-d59a935871db/v2.0',
                emulatorAuthV32IssuerV1: 'https://sts.windows.net/f8cdef31-a31e-4b4a-93e4-5f571e91255a/',
                emulatorAuthV32IssuerV2: 'https://login.microsoftonline.com/f8cdef31-a31e-4b4a-93e4-5f571e91255a/v2.0',
                stateEndpoint: this.settings.stateEndpoint || 'https://state.botframework.com'
            };
        }
        this.botConnectorOpenIdMetadata = new OpenIdMetadata_1.OpenIdMetadata(this.settings.endpoint.botConnectorOpenIdMetadata);
        this.emulatorOpenIdMetadata = new OpenIdMetadata_1.OpenIdMetadata(this.settings.endpoint.emulatorOpenIdMetadata);
    }
    ChatConnector.prototype.listen = function () {
        var _this = this;
        function defaultNext() { }
        return function (req, res, next) {
            if (req.body) {
                _this.verifyBotFramework(req, res, next || defaultNext);
            }
            else {
                var requestData = '';
                req.on('data', function (chunk) {
                    requestData += chunk;
                });
                req.on('end', function () {
                    try {
                        req.body = JSON.parse(requestData);
                    }
                    catch (err) {
                        logger.error('ChatConnector: receive - invalid request data received.');
                        res.send(400);
                        res.end();
                        return;
                    }
                    _this.verifyBotFramework(req, res, next || defaultNext);
                });
            }
        };
    };
    ChatConnector.prototype.verifyBotFramework = function (req, res, next) {
        var _this = this;
        var token;
        var isEmulator = req.body['channelId'] === 'emulator';
        var authHeaderValue = req.headers ? req.headers['authorization'] || req.headers['Authorization'] : null;
        if (authHeaderValue) {
            var auth = authHeaderValue.trim().split(' ');
            if (auth.length == 2 && auth[0].toLowerCase() == 'bearer') {
                token = auth[1];
            }
        }
        if (token) {
            var decoded_1 = jwt.decode(token, { complete: true });
            var verifyOptions;
            var openIdMetadata;
            var algorithms = ['RS256', 'RS384', 'RS512'];
            if (isEmulator) {
                if ((decoded_1.payload.ver === '2.0' && decoded_1.payload.azp !== this.settings.appId) ||
                    (decoded_1.payload.ver !== '2.0' && decoded_1.payload.appid !== this.settings.appId)) {
                    logger.error('ChatConnector: receive - invalid token. Requested by unexpected app ID.');
                    res.status(403);
                    res.end();
                    next();
                    return;
                }
                var issuer = void 0;
                if (decoded_1.payload.ver === '1.0' && decoded_1.payload.iss == this.settings.endpoint.emulatorAuthV31IssuerV1) {
                    issuer = this.settings.endpoint.emulatorAuthV31IssuerV1;
                }
                else if (decoded_1.payload.ver === '2.0' && decoded_1.payload.iss == this.settings.endpoint.emulatorAuthV31IssuerV2) {
                    issuer = this.settings.endpoint.emulatorAuthV31IssuerV2;
                }
                else if (decoded_1.payload.ver === '1.0' && decoded_1.payload.iss == this.settings.endpoint.emulatorAuthV32IssuerV1) {
                    issuer = this.settings.endpoint.emulatorAuthV32IssuerV1;
                }
                else if (decoded_1.payload.ver === '2.0' && decoded_1.payload.iss == this.settings.endpoint.emulatorAuthV32IssuerV2) {
                    issuer = this.settings.endpoint.emulatorAuthV32IssuerV2;
                }
                if (issuer) {
                    openIdMetadata = this.emulatorOpenIdMetadata;
                    verifyOptions = {
                        algorithms: algorithms,
                        issuer: issuer,
                        audience: this.settings.endpoint.emulatorAudience,
                        clockTolerance: 300
                    };
                }
            }
            if (!verifyOptions) {
                openIdMetadata = this.botConnectorOpenIdMetadata;
                verifyOptions = {
                    issuer: this.settings.endpoint.botConnectorIssuer,
                    audience: this.settings.endpoint.botConnectorAudience,
                    clockTolerance: 300
                };
            }
            openIdMetadata.getKey(decoded_1.header.kid, function (key) {
                if (key) {
                    try {
                        jwt.verify(token, key.key, verifyOptions);
                        if (typeof req.body.channelId !== 'undefined' &&
                            typeof key.endorsements !== 'undefined' &&
                            key.endorsements.lastIndexOf(req.body.channelId) === -1) {
                            var errorDescription = "channelId in req.body: " + req.body.channelId + " didn't match the endorsements: " + key.endorsements.join(',') + ".";
                            logger.error("ChatConnector: receive - endorsements validation failure. " + errorDescription);
                            throw new Error(errorDescription);
                        }
                        if (typeof decoded_1.payload.serviceurl !== 'undefined' &&
                            typeof req.body.serviceUrl !== 'undefined' &&
                            decoded_1.payload.serviceurl !== req.body.serviceUrl) {
                            var errorDescription = "ServiceUrl in payload of token: " + decoded_1.payload.serviceurl + " didn't match the request's serviceurl: " + req.body.serviceUrl + ".";
                            logger.error("ChatConnector: receive - serviceurl mismatch. " + errorDescription);
                            throw new Error(errorDescription);
                        }
                    }
                    catch (err) {
                        logger.error('ChatConnector: receive - invalid token. Check bot\'s app ID & Password.');
                        res.send(403, err);
                        res.end();
                        next();
                        return;
                    }
                    _this.dispatch(req.body, res, next);
                }
                else {
                    logger.error('ChatConnector: receive - invalid signing key or OpenId metadata document.');
                    res.status(500);
                    res.end();
                    next();
                    return;
                }
            });
        }
        else if (isEmulator && !this.settings.appId && !this.settings.appPassword) {
            logger.warn(req.body, 'ChatConnector: receive - emulator running without security enabled.');
            this.dispatch(req.body, res, next);
        }
        else {
            logger.error('ChatConnector: receive - no security token sent.');
            res.status(401);
            res.end();
            next();
        }
    };
    ChatConnector.prototype.onEvent = function (handler) {
        this.onEventHandler = handler;
    };
    ChatConnector.prototype.onInvoke = function (handler) {
        this.onInvokeHandler = handler;
    };
    ChatConnector.prototype.send = function (messages, done) {
        var _this = this;
        var addresses = [];
        async.forEachOfSeries(messages, function (msg, idx, cb) {
            try {
                if (msg.type == 'delay') {
                    setTimeout(cb, msg.value);
                }
                else {
                    var addressExists = !!msg.address;
                    var serviceUrlExists = addressExists && !!msg.address.serviceUrl;
                    if (serviceUrlExists) {
                        _this.postMessage(msg, (idx == messages.length - 1), function (err, address) {
                            addresses.push(address);
                            cb(err);
                        });
                    }
                    else {
                        var msg_1 = "Message is missing " + (addressExists ? 'address and serviceUrl' : 'serviceUrl') + " ";
                        logger.error("ChatConnector: send - " + msg_1);
                        cb(new Error(msg_1));
                    }
                }
            }
            catch (e) {
                cb(e);
            }
        }, function (err) { return done(err, !err ? addresses : null); });
    };
    ChatConnector.prototype.startConversation = function (address, done) {
        if (address && address.user && address.bot && address.serviceUrl) {
            var options = {
                method: 'POST',
                url: urlJoin(address.serviceUrl, '/v3/conversations'),
                body: {
                    bot: address.bot,
                    members: address.members || [address.user]
                },
                json: true
            };
            if (address.activity) {
                options.body.activity = address.activity;
            }
            if (address.channelData) {
                options.body.channelData = address.channelData;
            }
            if (address.isGroup !== undefined) {
                options.body.isGroup = address.isGroup;
            }
            if (address.topicName) {
                options.body.topicName = address.topicName;
            }
            this.authenticatedRequest(options, function (err, response, body) {
                var adr;
                if (!err) {
                    try {
                        var obj = typeof body === 'string' ? JSON.parse(body) : body;
                        if (obj && obj.hasOwnProperty('id')) {
                            adr = utils.clone(address);
                            adr.conversation = { id: obj['id'] };
                            if (obj['serviceUrl']) {
                                adr.serviceUrl = obj['serviceUrl'];
                            }
                            if (adr.id) {
                                delete adr.id;
                            }
                        }
                        else {
                            err = new Error('Failed to start conversation: no conversation ID returned.');
                        }
                    }
                    catch (e) {
                        err = e instanceof Error ? e : new Error(e.toString());
                    }
                }
                if (err) {
                    logger.error('ChatConnector: startConversation - error starting conversation.');
                }
                done(err, adr);
            });
        }
        else {
            logger.error('ChatConnector: startConversation - address is invalid.');
            done(new Error('Invalid address.'));
        }
    };
    ChatConnector.prototype.update = function (message, done) {
        var address = message.address;
        if (message.address && address.serviceUrl) {
            message.id = address.id;
            this.postMessage(message, true, done, 'PUT');
        }
        else {
            logger.error('ChatConnector: updateMessage - message is missing address or serviceUrl.');
            done(new Error('Message missing address or serviceUrl.'), null);
        }
    };
    ChatConnector.prototype.delete = function (address, done) {
        var path = '/v3/conversations/' + encodeURIComponent(address.conversation.id) +
            '/activities/' + encodeURIComponent(address.id);
        var options = {
            method: 'DELETE',
            url: urlJoin(address.serviceUrl, path),
            json: true
        };
        this.authenticatedRequest(options, function (err, response, body) { return done(err); });
    };
    ChatConnector.prototype.getData = function (context, callback) {
        var _this = this;
        try {
            console.warn(StateApiDreprecatedMessage);
            var root = this.getStoragePath(context.address);
            var list = [];
            if (context.userId) {
                if (context.persistUserData) {
                    list.push({
                        field: 'userData',
                        url: root + '/users/' + encodeURIComponent(context.userId)
                    });
                }
                if (context.conversationId) {
                    list.push({
                        field: 'privateConversationData',
                        url: root + '/conversations/' + encodeURIComponent(context.conversationId) +
                            '/users/' + encodeURIComponent(context.userId)
                    });
                }
            }
            if (context.persistConversationData && context.conversationId) {
                list.push({
                    field: 'conversationData',
                    url: root + '/conversations/' + encodeURIComponent(context.conversationId)
                });
            }
            var data = {};
            async.each(list, function (entry, cb) {
                var options = {
                    method: 'GET',
                    url: entry.url,
                    json: true
                };
                _this.authenticatedRequest(options, function (err, response, body) {
                    if (!err && body) {
                        var botData = body.data ? body.data : {};
                        if (typeof botData === 'string') {
                            zlib.gunzip(new Buffer(botData, 'base64'), function (err, result) {
                                if (!err) {
                                    try {
                                        var txt = result.toString();
                                        data[entry.field + 'Hash'] = txt;
                                        data[entry.field] = JSON.parse(txt);
                                    }
                                    catch (e) {
                                        err = e;
                                    }
                                }
                                cb(err);
                            });
                        }
                        else {
                            try {
                                data[entry.field + 'Hash'] = JSON.stringify(botData);
                                data[entry.field] = botData;
                            }
                            catch (e) {
                                err = e;
                            }
                            cb(err);
                        }
                    }
                    else {
                        cb(err);
                    }
                });
            }, function (err) {
                if (!err) {
                    callback(null, data);
                }
                else {
                    var m = err.toString();
                    callback(err instanceof Error ? err : new Error(m), null);
                }
            });
        }
        catch (e) {
            callback(e instanceof Error ? e : new Error(e.toString()), null);
        }
    };
    ChatConnector.prototype.saveData = function (context, data, callback) {
        var _this = this;
        console.warn(StateApiDreprecatedMessage);
        var list = [];
        function addWrite(field, botData, url) {
            var hashKey = field + 'Hash';
            var hash = JSON.stringify(botData);
            if (!data[hashKey] || hash !== data[hashKey]) {
                data[hashKey] = hash;
                list.push({ botData: botData, url: url, hash: hash });
            }
        }
        try {
            var root = this.getStoragePath(context.address);
            if (context.userId) {
                if (context.persistUserData) {
                    addWrite('userData', data.userData || {}, root + '/users/' + encodeURIComponent(context.userId));
                }
                if (context.conversationId) {
                    var url = root + '/conversations/' + encodeURIComponent(context.conversationId) +
                        '/users/' + encodeURIComponent(context.userId);
                    addWrite('privateConversationData', data.privateConversationData || {}, url);
                }
            }
            if (context.persistConversationData && context.conversationId) {
                addWrite('conversationData', data.conversationData || {}, root + '/conversations/' + encodeURIComponent(context.conversationId));
            }
            async.each(list, function (entry, cb) {
                if (_this.settings.gzipData) {
                    zlib.gzip(entry.hash, function (err, result) {
                        if (!err && result.length > MAX_DATA_LENGTH) {
                            err = new Error("Data of " + result.length + " bytes gzipped exceeds the " + MAX_DATA_LENGTH + " byte limit. Can't post to: " + entry.url);
                            err.code = consts.Errors.EMSGSIZE;
                        }
                        if (!err) {
                            var options = {
                                method: 'POST',
                                url: entry.url,
                                body: { eTag: '*', data: result.toString('base64') },
                                json: true
                            };
                            _this.authenticatedRequest(options, function (err, response, body) {
                                cb(err);
                            });
                        }
                        else {
                            cb(err);
                        }
                    });
                }
                else if (entry.hash.length < MAX_DATA_LENGTH) {
                    var options = {
                        method: 'POST',
                        url: entry.url,
                        body: { eTag: '*', data: entry.botData },
                        json: true
                    };
                    _this.authenticatedRequest(options, function (err, response, body) {
                        cb(err);
                    });
                }
                else {
                    var err = new Error("Data of " + entry.hash.length + " bytes exceeds the " + MAX_DATA_LENGTH + " byte limit. Consider setting connectors gzipData option. Can't post to: " + entry.url);
                    err.code = consts.Errors.EMSGSIZE;
                    cb(err);
                }
            }, function (err) {
                if (callback) {
                    if (!err) {
                        callback(null);
                    }
                    else {
                        var m = err.toString();
                        callback(err instanceof Error ? err : new Error(m));
                    }
                }
            });
        }
        catch (e) {
            if (callback) {
                var err = e instanceof Error ? e : new Error(e.toString());
                err.code = consts.Errors.EBADMSG;
                callback(err);
            }
        }
    };
    ChatConnector.prototype.onDispatchEvents = function (events, callback) {
        if (events && events.length > 0) {
            if (this.isInvoke(events[0])) {
                this.onInvokeHandler(events[0], callback);
            }
            else {
                this.onEventHandler(events);
                callback(null, null, 202);
            }
        }
    };
    ChatConnector.prototype.dispatch = function (msg, res, next) {
        try {
            this.prepIncomingMessage(msg);
            logger.info(msg, 'ChatConnector: message received.');
            this.onDispatchEvents([msg], function (err, body, status) {
                if (err) {
                    res.status(500);
                    res.end();
                    next();
                    logger.error('ChatConnector: error dispatching event(s) - ', err.message || '');
                }
                else if (body) {
                    res.send(status || 200, body);
                    res.end();
                    next();
                }
                else {
                    res.status(status || 200);
                    res.end();
                    next();
                }
            });
        }
        catch (e) {
            console.error(e instanceof Error ? e.stack : e.toString());
            res.status(500);
            res.end();
            next();
        }
    };
    ChatConnector.prototype.isInvoke = function (event) {
        return (event && event.type && event.type.toLowerCase() == consts.invokeType);
    };
    ChatConnector.prototype.postMessage = function (msg, lastMsg, cb, method) {
        if (method === void 0) { method = 'POST'; }
        logger.info(address, 'ChatConnector: sending message.');
        this.prepOutgoingMessage(msg);
        var address = msg.address;
        msg['from'] = address.bot;
        msg['recipient'] = address.user;
        delete msg.address;
        if (msg.type === 'message' && !msg.inputHint) {
            msg.inputHint = lastMsg ? 'acceptingInput' : 'ignoringInput';
        }
        var path = '/v3/conversations/' + encodeURIComponent(address.conversation.id) + '/activities';
        if (address.id && address.channelId !== 'skype') {
            path += '/' + encodeURIComponent(address.id);
        }
        var options = {
            method: method,
            url: urlJoin(address.serviceUrl, path),
            body: msg,
            json: true
        };
        this.authenticatedRequest(options, function (err, response, body) {
            if (!err) {
                if (body && body.id) {
                    var newAddress = utils.clone(address);
                    newAddress.id = body.id;
                    cb(null, newAddress);
                }
                else {
                    cb(null, address);
                }
            }
            else {
                cb(err, null);
            }
        });
    };
    ChatConnector.prototype.authenticatedRequest = function (options, callback, refresh) {
        var _this = this;
        if (refresh === void 0) { refresh = false; }
        if (refresh) {
            this.accessToken = null;
        }
        this.addUserAgent(options);
        this.addAccessToken(options, function (err) {
            if (!err) {
                request(options, function (err, response, body) {
                    if (!err) {
                        switch (response.statusCode) {
                            case 401:
                            case 403:
                                if (!refresh && _this.settings.appId && _this.settings.appPassword) {
                                    _this.authenticatedRequest(options, callback, true);
                                }
                                else {
                                    callback(null, response, body);
                                }
                                break;
                            default:
                                if (response.statusCode < 400) {
                                    callback(null, response, body);
                                }
                                else {
                                    var txt = options.method + " to '" + options.url + "' failed: [" + response.statusCode + "] " + response.statusMessage;
                                    callback(new Error(txt), response, null);
                                }
                                break;
                        }
                    }
                    else {
                        callback(err, null, null);
                    }
                });
            }
            else {
                callback(err, null, null);
            }
        });
    };
    ChatConnector.prototype.tokenExpired = function () {
        return Date.now() >= this.accessTokenExpires;
    };
    ChatConnector.prototype.tokenHalfWayExpired = function (secondstoHalfWayExpire, secondsToExpire) {
        if (secondstoHalfWayExpire === void 0) { secondstoHalfWayExpire = 1800; }
        if (secondsToExpire === void 0) { secondsToExpire = 300; }
        var timeToExpiration = (this.accessTokenExpires - Date.now()) / 1000;
        return timeToExpiration < secondstoHalfWayExpire
            && timeToExpiration > secondsToExpire;
    };
    ChatConnector.prototype.refreshAccessToken = function (cb) {
        var _this = this;
        if (!this.refreshingToken) {
            this.refreshingToken = new Promise(function (resolve, reject) {
                var opt = {
                    method: 'POST',
                    url: _this.settings.endpoint.refreshEndpoint,
                    form: {
                        grant_type: 'client_credentials',
                        client_id: _this.settings.appId,
                        client_secret: _this.settings.appPassword,
                        scope: _this.settings.endpoint.refreshScope
                    }
                };
                _this.addUserAgent(opt);
                request(opt, function (err, response, body) {
                    if (!err) {
                        if (body && response.statusCode < 300) {
                            var oauthResponse = JSON.parse(body);
                            _this.accessToken = oauthResponse.access_token;
                            _this.accessTokenExpires = new Date().getTime() + ((oauthResponse.expires_in - 300) * 1000);
                            _this.refreshingToken = undefined;
                            resolve(_this.accessToken);
                        }
                        else {
                            reject(new Error('Refresh access token failed with status code: ' + response.statusCode));
                        }
                    }
                    else {
                        reject(err);
                    }
                });
            });
        }
        this.refreshingToken.then(function (token) { return cb(null, token); }, function (err) { return cb(err, null); });
    };
    ChatConnector.prototype.getAccessToken = function (cb) {
        var _this = this;
        if (this.accessToken == null || this.tokenExpired()) {
            this.refreshAccessToken(function (err, token) {
                cb(err, _this.accessToken);
            });
        }
        else if (this.tokenHalfWayExpired()) {
            var oldToken = this.accessToken;
            this.refreshAccessToken(function (err, token) {
                if (!err)
                    cb(null, _this.accessToken);
                else
                    cb(null, oldToken);
            });
        }
        else
            cb(null, this.accessToken);
    };
    ChatConnector.prototype.addUserAgent = function (options) {
        if (!options.headers) {
            options.headers = {};
        }
        options.headers['User-Agent'] = USER_AGENT;
    };
    ChatConnector.prototype.addAccessToken = function (options, cb) {
        if (this.settings.appId && this.settings.appPassword) {
            this.getAccessToken(function (err, token) {
                if (!err && token) {
                    if (!options.headers) {
                        options.headers = {};
                    }
                    options.headers['Authorization'] = 'Bearer ' + token;
                    cb(null);
                }
                else {
                    cb(err);
                }
            });
        }
        else {
            cb(null);
        }
    };
    ChatConnector.prototype.getStoragePath = function (address) {
        var path;
        switch (address.channelId) {
            case 'emulator':
                if (address.serviceUrl) {
                    path = address.serviceUrl;
                }
                else {
                    throw new Error('ChatConnector.getStoragePath() missing address.serviceUrl.');
                }
                break;
            default:
                path = this.settings.endpoint.stateEndpoint;
                break;
        }
        return path + '/v3/botstate/' + encodeURIComponent(address.channelId);
    };
    ChatConnector.prototype.prepIncomingMessage = function (msg) {
        utils.moveFieldsTo(msg, msg, {
            'locale': 'textLocale',
            'channelData': 'sourceEvent'
        });
        msg.text = msg.text || '';
        msg.attachments = msg.attachments || [];
        msg.entities = msg.entities || [];
        var address = {};
        utils.moveFieldsTo(msg, address, toAddress);
        msg.address = address;
        msg.source = address.channelId;
        if (msg.source == 'facebook' && msg.sourceEvent && msg.sourceEvent.message && msg.sourceEvent.message.quick_reply) {
            msg.text = msg.sourceEvent.message.quick_reply.payload;
        }
    };
    ChatConnector.prototype.prepOutgoingMessage = function (msg) {
        if (msg.attachments) {
            var attachments = [];
            for (var i = 0; i < msg.attachments.length; i++) {
                var a = msg.attachments[i];
                switch (a.contentType) {
                    case 'application/vnd.microsoft.keyboard':
                        if (msg.address.channelId == 'facebook') {
                            msg.sourceEvent = { quick_replies: [] };
                            a.content.buttons.forEach(function (action) {
                                switch (action.type) {
                                    case 'imBack':
                                    case 'postBack':
                                        msg.sourceEvent.quick_replies.push({
                                            content_type: 'text',
                                            title: action.title,
                                            payload: action.value
                                        });
                                        break;
                                    default:
                                        logger.warn(msg, "Invalid keyboard '%s' button sent to facebook.", action.type);
                                        break;
                                }
                            });
                        }
                        else {
                            a.contentType = 'application/vnd.microsoft.card.hero';
                            attachments.push(a);
                        }
                        break;
                    default:
                        attachments.push(a);
                        break;
                }
            }
            msg.attachments = attachments;
        }
        utils.moveFieldsTo(msg, msg, {
            'textLocale': 'locale',
            'sourceEvent': 'channelData'
        });
        delete msg.agent;
        delete msg.source;
        if (!msg.localTimestamp) {
            msg.localTimestamp = new Date().toISOString();
        }
    };
    return ChatConnector;
}());
exports.ChatConnector = ChatConnector;
var toAddress = {
    'id': 'id',
    'channelId': 'channelId',
    'from': 'user',
    'conversation': 'conversation',
    'recipient': 'bot',
    'serviceUrl': 'serviceUrl'
};
