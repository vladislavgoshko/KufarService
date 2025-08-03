namespace WebApplicationAPI.Tools
{
    public class CategoryTools
    {
        /// <summary>
        /// Calculates the parent ID for a given category ID by truncating it to the nearest thousand.
        /// E.g., 1234 returns 1000, 567 returns 0.
        /// </summary>
        /// <param name="categoryId">The category ID.</param>
        /// <returns>The parent ID.</returns>
        public static int GetParentCategoryId(int categoryId)
        {
            return categoryId / 1000 * 1000;
        }
    }
}
