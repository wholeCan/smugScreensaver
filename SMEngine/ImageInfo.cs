namespace SMEngine
{
    /// <summary>
    /// Originally written -- 4/2014
    /// 
    /// revision 5/8/2018:  Switched to Smugmug api 1.3 by upgrading to nuget version of SmugMugModel
    /// SmugMug API is now available from nuget
    /// 
    /// 2/26/2022: major refactor to upgrade to smugmug 2.0 api

    //    Following along source from:
    //https://github.com/AlexGhiondea/SmugMug.NET/blob/master/nuGet/SmugMugModel.v2.nuspec

    //Need to better understand the api
    //https://api.smugmug.com/api/v2/doc/pages/concepts.html

    ///
    /// 2018 feature enhancements:
    /// put a timeout period, stop pulling images after a couple hours.  restart after 24 hours.
    /// </summary>

    //is this class needeD? maybe not
    public class ImageInfo
    {
        string key;
        string caption;
        string name;
    }

}
