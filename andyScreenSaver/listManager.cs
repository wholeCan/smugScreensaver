using System;
using System.Collections.Generic;
namespace andyScreenSaver
{
    public class listManager : Queue<int>
    {

        int maxItems;
        List<Tuple<int, int>> myList = new List<Tuple<int, int>>();
        private listManager() { }
        public listManager(int _maxItems)
        {
            maxItems = _maxItems;
        }
        public bool isInList(Tuple<int, int> item)
        {
            bool isFound = (myList.FindIndex(i => i.Equals(item)) >= 0);
            return isFound;
        }
        public void addToList(Tuple<int, int> item)
        {
            if (!isInList(item))
            {
                myList.Add(item);
                if (myList.Count >= maxItems)
                {
                    myList.Clear();
                }
            }
        }
    }
}
