namespace WebApplicationAPI.Models.Category
{
    public class Subcategory
    {
        public string Id { get; set; }
        public string Parent { get; set; }
        public string Order { get; set; }
        public string Name { get; set; }
        public string Name_ru { get; set; }
        public string Name_by { get; set; }
        public string? Redirect { get; set; }

        /// <summary>
        /// Конструктор для создания экземпляра Subcategory со всеми свойствами.
        /// </summary>
        public Subcategory(string id, string parent, string order, string name, string name_ru, string name_by, string? redirect = null)
        {
            Id = id;
            Parent = parent;
            Order = order;
            Name = name;
            Name_ru = name_ru;
            Name_by = name_by;
            Redirect = redirect;
        }

        /// <summary>
        /// Конструктор по умолчанию. Необходим для десериализации JSON.
        /// </summary>
        public Subcategory()
        {
            // Пустой конструктор по умолчанию
        }
    }
}