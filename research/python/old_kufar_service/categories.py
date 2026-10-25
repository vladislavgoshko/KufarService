class Subcategorie:
    def __init__(self, id: int, name: str, name_ru: str, name_by: str, order: int, parent: int) -> None:
        self.id = id
        self.parent = parent
        self.order = order
        self.name = name
        self.name_ru = name_ru
        self.name_by = name_by

    def __str__(self):
        return (f"Subcategorie ID: {self.id}, Parent ID: {self.parent}\n"
                f"Name: {self.name}, Name (RU): {self.name_ru}, Name (BY): {self.name_by}\n"
                f"Order: {self.order}")
    
    
class Categorie:
    def __init__(self, id: int, name: str, name_ru: str, name_by: str, version: int, order: int, subcategories: list[Subcategorie] = []) -> None:
        self.id = id
        self.name = name
        self.name_ru = name_ru
        self.name_by = name_by
        self.version = version
        self.order = order
        self.subcategories = subcategories

    def __str__(self):
        subcategories_str = "\n".join([str(sub) for sub in self.subcategories])
        return (f"Categorie ID: {self.id}\n"
                f"Name: {self.name}, Name (RU): {self.name_ru}, Name (BY): {self.name_by}\n"
                f"Version: {self.version}, Order: {self.order}\n"
                f"Subcategories:\n{subcategories_str if subcategories_str else 'No subcategories'}")


