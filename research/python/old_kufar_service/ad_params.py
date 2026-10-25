from api_data import APIData

class AdParams:
    def __init__(self, category: int, min_price: float = 0, max_price: float = 999999999999, 
                 other_parameters: dict = {}, 
                 size: int = 50, sort = 'lst.d', cur = 'BYN') -> None:
        self.category = category
        self.min_price = min_price
        self.max_price = max_price
        
        self.other_parameters = other_parameters

        self.size = size
        self.sort = sort
        self.cur = cur
    
    def get_url(self):
        return f"https://api.kufar.by/search-api/v2/search/rendered-paginated?{self.__params_to_str__()}"
    
    def __params_to_str__(self):
        params_str = f"size={self.size}&sort={self.sort}&cur={self.cur}"
        params_str += f"&cat={self.category}"
        params_str += f"&prc=r:{self.min_price},{self.max_price}"
        params_str += self.__get_parsed_other_params__()
        return params_str
    
    def __get_parsed_other_params__(self):
        return ""
    
if __name__ == "__main__":
    ad_params = AdParams(category=5020)
    print(ad_params.get_url())
