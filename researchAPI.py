# import requests
# import json

# BASE_URL = "https://api.kufar.by/taxonomy-proxy/v1/dispatch?routing=web_generalist&application=ad_listing&platform=web&taxonomy-version=1&parent={parentId}"

# unique_keys = set()
# unique_values = dict()
# all_data = []

# def collect_keys_values(obj):
#     if isinstance(obj, dict):
#         for k, v in obj.items():
#             unique_keys.add(k)
#             if k not in unique_values:
#                 unique_values[k] = set()
#             if isinstance(v, (str, int, float, bool, type(None))):
#                 unique_values[k].add(v)
#             collect_keys_values(v)
#     elif isinstance(obj, list):
#         for item in obj:
#             collect_keys_values(item)

# for parent_id in range(1000, 21001, 1000):
#     url = BASE_URL.format(parentId=parent_id)
#     try:
#         resp = requests.get(url)
#         resp.raise_for_status()
#         data = resp.json()
#         all_data.append(data)
#         collect_keys_values(data)
#         print(f"Получен parentId={parent_id}")
#     except Exception as e:
#         print(f"Ошибка для parentId={parent_id}: {e}")

# # Сохраняем уникальные ключи и значения в файл
# result = {
#     "unique_keys": list(unique_keys),
#     "unique_values": {k: list(v) for k, v in unique_values.items()}
# }

# with open("unique_keys_and_values.json", "w", encoding="utf-8") as f:
#     json.dump(result, f, ensure_ascii=False, indent=2)

# print("Готово! Результат сохранён в unique_keys_and_values.json")


import requests
import json

BASE_URL = "https://api.kufar.by/taxonomy-proxy/v1/dispatch?routing=web_generalist&application=ad_listing&platform=web&taxonomy-version=1&parent={parentId}"

unique_paths = set()
unique_values = dict()

def collect_paths_values(obj, path=""):
    if isinstance(obj, dict):
        for k, v in obj.items():
            # Если ключ — число и мы находимся внутри refs, заменяем на {id}
            if path.endswith(".refs") and k.isdigit():
                new_path = f"{path}.{{id}}"
            else:
                new_path = f"{path}.{k}" if path else k
            unique_paths.add(new_path)
            if new_path not in unique_values:
                unique_values[new_path] = set()
            if isinstance(v, (str, int, float, bool, type(None))):
                unique_values[new_path].add(v)
            collect_paths_values(v, new_path)
    elif isinstance(obj, list):
        for item in obj:
            collect_paths_values(item, path)

for parent_id in range(1000, 21001, 1000):
    url = BASE_URL.format(parentId=parent_id)
    try:
        resp = requests.get(url)
        resp.raise_for_status()
        data = resp.json()
        collect_paths_values(data)
        print(f"Получен parentId={parent_id}")
    except Exception as e:
        print(f"Ошибка для parentId={parent_id}: {e}")

result = {
    "unique_paths": list(unique_paths),
    "unique_values": {k: list(v) for k, v in unique_values.items()}
}

with open("unique_paths_and_values.json", "w", encoding="utf-8") as f:
    json.dump(result, f, ensure_ascii=False, indent=2)

print("Готово! Результат сохранён в unique_paths_and_values.json")