using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Plugin.BetterFirebasePushNotification
{
    /// <summary>
    /// Rate limiter used to suppress duplicate notification presentations
    /// (e.g. iOS 18 firing willPresentNotification twice for the same notification).
    /// </summary>
    internal class NotificationRateLimiter
    {
        private readonly ConcurrentDictionary<string, DateTime> cache = new();

        public bool HasReachedLimit(string identifier, TimeSpan expirationTime)
        {
            if(identifier == null) {
                throw new ArgumentNullException(nameof(identifier), $"Parameter {nameof(identifier)} must not be null.");
            }

            if(expirationTime <= TimeSpan.Zero) {
                throw new ArgumentException($"Parameter {nameof(expirationTime)} must be greater than zero.", nameof(expirationTime));
            }

            var utcNow = DateTime.UtcNow;

            if(this.cache.TryGetValue(identifier, out var existingExpirationTime)) {
                if(existingExpirationTime > utcNow) {
                    return true;
                }
            }

            foreach(var item in this.cache) {
                if(item.Value > utcNow) {
                    this.cache.TryRemove(item.Key, out _);
                }
            }

            this.cache[identifier] = utcNow.Add(expirationTime);
            return false;
        }

        public int Count => this.cache.Count();
    }
}
