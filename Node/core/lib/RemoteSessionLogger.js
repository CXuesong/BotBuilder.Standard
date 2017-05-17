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
var SessionLogger_1 = require("./SessionLogger");
var RemoteSessionLogger = (function (_super) {
    __extends(RemoteSessionLogger, _super);
    function RemoteSessionLogger(connector, address, relatesTo) {
        var _this = _super.call(this) || this;
        _this.connector = connector;
        _this.address = address;
        _this.relatesTo = relatesTo;
        _this.isEnabled = true;
        _this.event = _this.createEvent();
        return _this;
    }
    RemoteSessionLogger.prototype.dump = function (name, value) {
        _super.prototype.dump.call(this, name, value);
        this.event.value.push({
            type: 'variable',
            timestamp: new Date().getTime(),
            name: name,
            value: value
        });
    };
    RemoteSessionLogger.prototype.log = function (dialogStack, msg) {
        var args = [];
        for (var _i = 2; _i < arguments.length; _i++) {
            args[_i - 2] = arguments[_i];
        }
        _super.prototype.log.apply(this, [dialogStack, msg].concat(args));
        this.event.value.push({
            type: 'log',
            timestamp: new Date().getTime(),
            level: 'info',
            msg: msg,
            args: args
        });
    };
    RemoteSessionLogger.prototype.warn = function (dialogStack, msg) {
        var args = [];
        for (var _i = 2; _i < arguments.length; _i++) {
            args[_i - 2] = arguments[_i];
        }
        _super.prototype.warn.apply(this, [dialogStack, msg].concat(args));
        this.event.value.push({
            type: 'log',
            timestamp: new Date().getTime(),
            level: 'warn',
            msg: msg,
            args: args
        });
    };
    RemoteSessionLogger.prototype.error = function (dialogStack, err) {
        _super.prototype.error.call(this, dialogStack, err);
        this.event.value.push({
            type: 'log',
            timestamp: new Date().getTime(),
            level: 'info',
            msg: err.stack
        });
    };
    RemoteSessionLogger.prototype.flush = function (callback) {
        var ev = this.event;
        this.event = this.createEvent();
        this.connector.send([ev], callback);
    };
    RemoteSessionLogger.prototype.createEvent = function () {
        return {
            type: 'event',
            address: this.address,
            name: 'debug',
            value: [],
            relatesTo: this.relatesTo,
            text: "Debug Event"
        };
    };
    return RemoteSessionLogger;
}(SessionLogger_1.SessionLogger));
exports.RemoteSessionLogger = RemoteSessionLogger;
