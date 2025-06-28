namespace WebApplicationAPI.Models.Category
{
    public class Category
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Name_ru { get; set; }
        public string Name_by { get; set; }
        public string Version { get; set; }
        public string Order { get; set; }
        public List<Subcategory> Subcategories { get; set; }

        // Конструктор
        public Category(string id, string name, string name_ru, string name_by, string version, string order, List<Subcategory> subcategories)
        {
            Id = id;
            Name = name;
            Name_ru = name_ru;
            Name_by = name_by;
            Version = version;
            Order = order;
            Subcategories = subcategories ?? new List<Subcategory>(); // Инициализация списка, если он null
        }

        // Конструктор без подкатегорий (на случай, если они не всегда есть)
        public Category(string id, string name, string name_ru, string name_by, string version, string order)
            : this(id, name, name_ru, name_by, version, order, null) // Вызываем основной конструктор с null для подкатегорий
        {
        }

        // Конструктор по умолчанию (без параметров)
        public Category()
        {
            Subcategories = new List<Subcategory>();
        }
    }
}