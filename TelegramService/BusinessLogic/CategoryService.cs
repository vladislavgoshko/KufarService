using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramService.BusinessLogic
{
    internal class CategoryService
    {
        public CategoryService()
        {
        }

        public List<Category> GetCategories()
        {
            return new List<Category> {
                new ("Машины", 1),
                new ("Капутеры", 2)
            };
            //return new List<Category>();
        }
        public List<Subcategory> GetSubcategories(int categoryId)
        {
            return new List<Subcategory>
            {
                new ("Ноутбуки", 22)
            };
        }
        public int GetCategoryIdBySubcategory(int subcategoryId)
        {
            return 2;
        }
    }
}
