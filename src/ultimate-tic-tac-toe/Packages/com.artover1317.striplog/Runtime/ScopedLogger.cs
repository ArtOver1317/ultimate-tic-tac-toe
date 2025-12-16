using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StripLog
{
    public readonly struct ScopedLogger
    {
        private readonly string _tag;

        public ScopedLogger(string tag) => _tag = tag;

        public void Debug(string message, Object context = null) => Log.Debug(_tag, message, context);
        public void Debug(Func<string> messageFactory, Object context = null) => Log.Debug(_tag, messageFactory, context);

        public void Info(string message, Object context = null) => Log.Info(_tag, message, context);
        public void Info(Func<string> messageFactory, Object context = null) => Log.Info(_tag, messageFactory, context);

        public void Warning(string message, Object context = null) => Log.Warning(_tag, message, context);
        public void Warning(Func<string> messageFactory, Object context = null) => Log.Warning(_tag, messageFactory, context);

        public void ErrorDev(string message, Object context = null) => Log.ErrorDev(_tag, message, context);

        public void Error(string message, Object context = null) => Log.Error(_tag, message, context);
        public void Exception(Exception ex) => Log.Exception(ex, _tag);
    }
}