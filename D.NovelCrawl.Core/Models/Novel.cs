﻿using D.NovelCrawl.Core.Interface;
using D.NovelCrawl.Core.Models;
using D.NovelCrawl.Core.Models.CrawlModel;
using D.NovelCrawl.Core.Models.DTO;
using D.Spider.Core;
using D.Spider.Core.Interface;
using D.Util.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace D.NovelCrawl.Core.Models
{
    /// <summary>
    /// 小说爬虫需要的所有信息
    /// </summary>
    internal class Novel
    {
        ILogger _logger;

        IUrlManager _urlManager;

        int _vipChapterNeedCrawlCount;
        IWebsitProxy _web;

        /// <summary>
        /// 非官方的目录url
        /// </summary>
        List<IUrl> _unofficialUrls = new List<IUrl>();

        #region 对外属性
        /// <summary>
        /// 小说 GUID
        /// </summary>
        public Guid Guid { get; private set; }

        /// <summary>
        /// 小说名称
        /// </summary>
        /// <returns></returns>
        public string Name { get; private set; }

        /// <summary>
        /// 卷信息
        /// </summary>
        public Dictionary<int, Volume> Volumes { get; private set; }

        /// <summary>
        /// 官网目录 Url
        /// </summary>
        public IUrl OfficialUrl { get; set; }

        public int VipChapterNeedCrawlCount
        {
            get
            {
                return _vipChapterNeedCrawlCount;
            }
            set
            {
                lock (this)
                {
                    _vipChapterNeedCrawlCount = value;
                }
            }
        }
        #endregion

        public Novel(
            ILoggerFactory loggerFactory,
            IUrlManager urlManager
            , IWebsitProxy web)
        {
            _logger = loggerFactory.CreateLogger<Novel>();
            _urlManager = urlManager;

            Volumes = new Dictionary<int, Volume>();
            _vipChapterNeedCrawlCount = 0;

            _web = web;
        }

        /// <summary>
        /// 设置小说相关的需要爬取的 url
        /// </summary>
        /// <param name="urls"></param>
        public void SetRelatedUrls(IEnumerable<NovelCrawlUrlModel> urls)
        {
            var official = urls.Where(uu => uu.Official).FirstOrDefault();

            if (official == null)
            {
                _logger.LogWarning(Name + " 没有设置对应的官网目录页面，不对其进行爬取");
                return;
            }

            if (OfficialUrl.String != official.Url)
            {
                OfficialUrl.Interval = -1;

                OfficialUrl = new Url(official.Url);
                OfficialUrl.CustomData = new UrlData
                {
                    NovelInfo = this,
                    Official = true,
                    Type = UrlTypes.NovleCatalog
                };
                OfficialUrl.Interval = 1800;

                OfficialUrl = _urlManager.AddUrl(OfficialUrl);
            }
            else
            {
                OfficialUrl.Recrwal();
            }


        }

        /// <summary>
        /// 根据从个人网站上获取的小说信息更新爬虫持有的小说信息
        /// </summary>
        /// <param name="model"></param>
        public void Update(NovelListModel model)
        {
            Guid = model.Guid;
            Name = model.Name;
        }

        /// <summary>
        /// 根据个人网站上记录的目录信息与爬虫持有的目录信息进行对比，判断哪些内容章节需要重新爬取
        /// 爬虫记录的小说目录信息需要与个人网站记录的目录信息同步
        /// 防止爬取到的章节上传失败或者某个章节报错需要重新爬取
        /// 在爬虫初始化之后，爬虫运行一段时间之后才需要调用这个函数
        /// </summary>
        /// <param name="volumes"></param>
        public void UpdateCatalog(NovelVolumeModel[] volumes)
        {
            lock (this)
            {
                Volumes.Clear();

                foreach (var v in volumes)
                {
                    var tv = new Volume()
                    {
                        Number = v.Number,
                        Name = v.Name
                    };

                    foreach (var c in v.Chapters)
                    {
                        var tc = new Chapter
                        {
                            ChapterNO = c.ChapterNO,
                            Name = c.Name,
                            Number = c.Number,
                            PublicTime = c.PublicTime,
                            ReCrawl = c.ReCrawl,
                            VipChapter = c.VipChapter,
                            WordCount = c.WordCount,
                            SourceUrl = c.SourceUrl,

                            VolumeNumber = tv.Number
                        };

                        tv.Chapters.Add(tc.Number, tc);
                    }

                    Volumes.Add(tv.Number, tv);
                }
            }
        }

        /// <summary>
        /// 将从官网获取到的目录信息与持有的信息对比，确定需要爬取的章节信息
        /// </summary>
        /// <param name="crawledVolumes"></param>
        public void CmpareOfficialCatalog(CrawlVolumeModel[] crawledVolumes)
        {
            _logger.LogInformation("共获得卷信息：" + crawledVolumes.Length);

            for (var i = 0; i < crawledVolumes.Length; i++)
            {
                var cv = crawledVolumes[i];
                Volume v;

                if (!Volumes.ContainsKey(i + 1))
                {
                    v = new Volume
                    {
                        Number = i + 1,
                        Name = cv.Name
                    };

                    Volumes.Add(v.Number, v);

                    _logger.LogInformation("未收录的卷：" + v.Name);
                }
                else
                {
                    v = Volumes[i + 1];
                }

                for (var j = 0; j < cv.Chapters.Length; j++)
                {
                    var cc = cv.Chapters[j];
                    Chapter c;

                    if (!v.Chapters.ContainsKey(j + 1))
                    {
                        c = new Chapter();

                        c.GUID = Guid.NewGuid();
                        c.Name = cc.Name;
                        c.Number = j + 1;
                        //PublicTime = DateTime.ParseExact(cc.PublicTime, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture),
                        c.ReCrawl = true;
                        c.VipChapter = string.IsNullOrEmpty(cc.Vip) ? false : true;
                        c.VolumeNumber = i + 1;
                        //c.WordCount = Convert.ToInt32(cc.WordCount);

                        v.Chapters.Add(c.Number, c);

                        if (c.VipChapter) VipChapterNeedCrawlCount++;
                    }
                    else
                    {
                        c = v.Chapters[j + 1];
                    }

                    if (c.ReCrawl && !c.VipChapter)
                    {
                        //如果需要爬取的章节不是 vip 章节，直接从官网获取章节的内容信息

                        IUrl url = OfficialUrl.CreateCompleteUrl(cc.Href);
                        url.CustomData = new ChapterTxtUrlData
                        {
                            NovelInfo = this,
                            Type = UrlTypes.NovleChapterTxt,
                            ChapterInfo = c
                        };
                        url.Interval = -1;

                        var inManager = _urlManager.AddUrl(url);
                        inManager.Recrwal();
                    }
                }
            }

            if (VipChapterNeedCrawlCount > 0)
            {

            }
        }

        /// <summary>
        /// 处理从小说章节正文页面爬取到的章节正文信息
        /// 并且对爬取到的小说内容进行一些处理
        /// </summary>
        /// <param name="chapter"></param>
        public void DealChapterCrwalData(Chapter chapter, CrawlChapterModel crawlData)
        {
            //1.去掉 html 标签
            var txt = RemoveHtmlTag(crawlData.Text);
            //2.判断字数
            var detail = new NovelChapterDetailModel()
            {
                ChapterGuid = chapter.GUID,
                ChapterName = chapter.Name,
                ChapterNO = chapter.ChapterNO,
                ChapterNumber = chapter.Number,
                ChapterTxt = txt,
                NovelGuid = this.Guid,
                VolumeName = chapter.VolumeNumber.ToString(),
                VolumeNumber = chapter.VolumeNumber
            };
            //3.上传到个人网站
            _web.UploadNovelChapter(detail);
        }

        /// <summary>
        /// 去除小说正文中的html标签
        /// 将 <br/> 替换为 \r
        /// </summary>
        /// <param name="txt"></param>
        /// <returns></returns>
        private string RemoveHtmlTag(string txt)
        {
            var tmp = Regex.Replace(txt, @"<br[^>]*>", "\r", RegexOptions.IgnoreCase);
            tmp = Regex.Replace(tmp, @"<[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            tmp = tmp
                .Replace(' ', '\0')
                .Replace('\t', '\0');
            return tmp;
        }
    }
}
