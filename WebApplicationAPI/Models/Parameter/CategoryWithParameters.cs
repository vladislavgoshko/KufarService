namespace WebApplicationAPI.Models.Parameter
{
    public class CategoryWithParameters
    {
        public string CategoryName { get; set; }
        public Dictionary<string, string> Rule { get; set; }
        public List<Parameter> Parameters { get; set; }

        public CategoryWithParameters()
        {
            Parameters = new List<Parameter>();
        }
    }
}
