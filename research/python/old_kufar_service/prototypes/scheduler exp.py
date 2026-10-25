from apscheduler.schedulers.background import BackgroundScheduler
from apscheduler.triggers.interval import IntervalTrigger
import time
import datetime

class MyClass:
    def __init__(self):
        pass

    def my_function(self, param):
        print(f"{param} {datetime.datetime.now().strftime('%M:%S')}")

# Создаем экземпляр класса
my_instance = MyClass()

# Создаем планировщик
scheduler = BackgroundScheduler()

for i in range(1000):
    job1 = scheduler.add_job(my_instance.my_function, IntervalTrigger(seconds=2), args=[f"1 {i}"])
    job2 = scheduler.add_job(my_instance.my_function, IntervalTrigger(seconds=5), args=[f"2 {i}"])

# Удаляем задачу по идентификатору
def remove_job(job_id):
    scheduler.remove_job(job_id)

# Запускаем планировщик
scheduler.start()

# Пример удаления задачи
time.sleep(12.1)  # Подождем 15 секунд
# remove_job(job1.id)  # Удаляем задачу job1

try:
    while True:
        time.sleep(1)
except (KeyboardInterrupt, SystemExit):
    scheduler.shutdown()
