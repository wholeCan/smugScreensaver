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


  hot keys:
  1. left/right arrow keys change photo
  2. s:  show or hide stats.  press it once, then wait for the next image to progress
  3. r:  reload the library.  useful if you want to pull in new galleries without restarting the whole app.


Pre-built installer: [LINK](https://github.com/wholeCan/smugScreensaver/raw/5b7fce5334c7a07f4cad62c01d6a6004045e19c1/nsisInstaller/andysScreensaverInstaller_small.exe)
