namespace TelegramService.BusinessLogic
{
    public class Category
    {
        public string Title { get; }
        public int Id { get; }
        public Category(string title, int id)
        {
            Title = title;
            Id = id;
        }

    }
}