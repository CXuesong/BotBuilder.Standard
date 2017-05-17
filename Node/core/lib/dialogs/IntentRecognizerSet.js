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
var utils = require("../utils");
var async = require("async");
var RecognizeOrder;
(function (RecognizeOrder) {
    RecognizeOrder[RecognizeOrder["parallel"] = 0] = "parallel";
    RecognizeOrder[RecognizeOrder["series"] = 1] = "series";
})(RecognizeOrder = exports.RecognizeOrder || (exports.RecognizeOrder = {}));
var IntentRecognizerSet = (function (_super) {
    __extends(IntentRecognizerSet, _super);
    function IntentRecognizerSet(options) {
        if (options === void 0) { options = {}; }
        var _this = _super.call(this) || this;
        _this.options = options;
        if (typeof _this.options.intentThreshold !== 'number') {
            _this.options.intentThreshold = 0.1;
        }
        if (!_this.options.hasOwnProperty('recognizeOrder')) {
            _this.options.recognizeOrder = RecognizeOrder.parallel;
        }
        if (!_this.options.recognizers) {
            _this.options.recognizers = [];
        }
        if (!_this.options.processLimit) {
            _this.options.processLimit = 4;
        }
        if (!_this.options.hasOwnProperty('stopIfExactMatch')) {
            _this.options.stopIfExactMatch = true;
        }
        _this.length = _this.options.recognizers.length;
        return _this;
    }
    IntentRecognizerSet.prototype.clone = function (copyTo) {
        var obj = copyTo || new IntentRecognizerSet(utils.clone(this.options));
        obj.options.recognizers = this.options.recognizers.slice(0);
        return obj;
    };
    IntentRecognizerSet.prototype.onRecognize = function (context, done) {
        if (this.options.recognizeOrder == RecognizeOrder.parallel) {
            this.recognizeInParallel(context, done);
        }
        else {
            this.recognizeInSeries(context, done);
        }
    };
    IntentRecognizerSet.prototype.recognizer = function (plugin) {
        this.options.recognizers.push(plugin);
        this.length++;
        return this;
    };
    IntentRecognizerSet.prototype.recognizeInParallel = function (context, done) {
        var _this = this;
        var result = { score: 0.0, intent: null };
        async.eachLimit(this.options.recognizers, this.options.processLimit, function (recognizer, cb) {
            try {
                recognizer.recognize(context, function (err, r) {
                    if (!err && r && r.score > result.score && r.score >= _this.options.intentThreshold) {
                        result = r;
                    }
                    cb(err);
                });
            }
            catch (e) {
                cb(e);
            }
        }, function (err) {
            if (!err) {
                done(null, result);
            }
            else {
                var msg = err.toString();
                done(err instanceof Error ? err : new Error(msg), null);
            }
        });
    };
    IntentRecognizerSet.prototype.recognizeInSeries = function (context, done) {
        var _this = this;
        var i = 0;
        var result = { score: 0.0, intent: null };
        async.whilst(function () {
            return (i < _this.options.recognizers.length && (result.score < 1.0 || !_this.options.stopIfExactMatch));
        }, function (cb) {
            try {
                var recognizer = _this.options.recognizers[i++];
                recognizer.recognize(context, function (err, r) {
                    if (!err && r && r.score > result.score && r.score >= _this.options.intentThreshold) {
                        result = r;
                    }
                    cb(err);
                });
            }
            catch (e) {
                cb(e);
            }
        }, function (err) {
            if (!err) {
                done(null, result);
            }
            else {
                done(err instanceof Error ? err : new Error(err.toString()), null);
            }
        });
    };
    return IntentRecognizerSet;
}(IntentRecognizer_1.IntentRecognizer));
exports.IntentRecognizerSet = IntentRecognizerSet;
