using System;
using System.Collections.Generic;
using System.Threading;

namespace andyScreenSaver
{
    //replicating task scheduler from engine.
    //12/22/2023
    public class TaskScheduler
    {

        private List<Timer> timers = new List<Timer>();
        private TaskScheduler() { }
        private static TaskScheduler _instance;
        public static TaskScheduler Instance => _instance ?? (_instance = new TaskScheduler());



        public void ScheduleTask(int hour, int min, double intervalInMinutes, Action task)
        {
            DateTime now = DateTime.Now;
            DateTime firstRun = new DateTime(now.Year, now.Month, now.Day, hour, min, 0, 0);
            if (now > firstRun)
            {
                firstRun = firstRun.AddDays(1);
            }

            TimeSpan timeToGo = firstRun - now;
            if (timeToGo <= TimeSpan.Zero)
            {
                timeToGo = TimeSpan.Zero;
            }

            var timer = new Timer(x =>
            {
                task.Invoke();
            }, null, timeToGo, TimeSpan.FromMinutes(intervalInMinutes));

            timers.Add(timer);
        }
    }
}
