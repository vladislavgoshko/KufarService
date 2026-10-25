using System.Collections.Concurrent;

namespace TelegramService.Services
{
    public class UserSession
    {
        public int? CategoryId { get; set; }
        public int? SubcategoryId { get; set; }
        public string City { get; set; }     // Для отображения в интерфейсе ("Минск")
        public int? CityId { get; set; }      // Для передачи в API (7)
    }

    public class UserSessionService
    {
        private readonly ConcurrentDictionary<long, UserSession> _sessions = new();

        public UserSession GetOrCreateSession(long chatId)
        {
            return _sessions.GetOrAdd(chatId, _ => new UserSession());
        }

        public void ClearSession(long chatId)
        {
            _sessions.TryRemove(chatId, out _);
        }
    }
}