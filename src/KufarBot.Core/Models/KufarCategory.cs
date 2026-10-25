using System.Collections.Generic;

namespace KufarBot.Core.Models;

public record KufarCategory(int Id, string Name, int Order, IReadOnlyList<KufarSubcategory> Subcategories);

public record KufarSubcategory(int Id, int ParentId, string Name, int Order);
