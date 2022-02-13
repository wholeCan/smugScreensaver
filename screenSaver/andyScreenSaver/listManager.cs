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
        public bool isInList(Tuple<int, int> x)
        {
            bool isFound = (myList.FindIndex(i => i.Equals(x)) >= 0);

            //return false;
            return isFound;


        }
        public void addToList(Tuple<int, int> x)
        {
            if (!isInList(x))
            {
                myList.Add(x);
                if (myList.Count >= maxItems)
                {
                    myList.Clear();
                }
            }
        }
    }
}
