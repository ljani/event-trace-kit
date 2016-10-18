﻿namespace EventTraceKit.VsExtension.Serialization
{
    using System;
    using System.Collections.Generic;

    public static class DictionaryExtensions
    {
        public static TValue GetOrAdd<TKey, TValue>(
            this IDictionary<TKey, TValue> dictionary, TKey key,
            Func<TKey, TValue> valueFactory)
        {
            TValue value;
            if (dictionary.TryGetValue(key, out value))
                return value;

            value = valueFactory(key);
            dictionary.Add(key, value);

            return value;
        }
    }
}