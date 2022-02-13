# smugScreensaver
Andys smugmug screensaver

Started in 2014, this code base is a hobby to build a screensaver based on a smugmug photo library
- there are some really bad software patterns
- major updates performed in 2018, and then 2022.


### requirements:
- I wanted 0 hard disk usage, or at least as little as possible.  the photo URLs are loaded into memory at startup, and there is an engine that maintains about 10 images in memory at any given time.
- 3 threads:
  1. image link collection
  2. photo collection
  3. UI thread, building out the screensaver
