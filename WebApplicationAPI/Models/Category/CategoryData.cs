namespace WebApplicationAPI.Models.Category
{
    public class CategoryData
    {
        public List<Category> Categories { get; set; }

        public CategoryData()
        {
            Categories = new List<Category>();
        }
    }

}
