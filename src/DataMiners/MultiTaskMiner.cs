using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RobloxClientTracker
{
    /// <summary>
    /// An extension of the DataMiner class, which allows multiple
    /// routines to be declared and executed in parallel.
    /// </summary>
    public abstract class MultiTaskMiner : DataMiner
    {
        private readonly List<Action> routines = new List<Action>();

        protected void addRoutine(Action routine)
        {
            routines.Add(routine);
        }

        public override void ExecuteRoutine()
        {
            var tasks = new List<Task>();

            foreach (Action routine in routines)
            {
                Task task = Task.Run(routine);
                tasks.Add(task);
            }

            Task multiTask = Task.WhenAll(tasks);
            multiTask.Wait();
        }
    }
}
