using Api.Services.Interfaces;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace Api.Services;

public class TassWebDriverService : ITassWebDriverService
{
    private readonly ChromeDriver _driver;

    public TassWebDriverService()
    {
        var options = new ChromeOptions();
        
        const string userAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/126.0.0.0 Safari/537.36";

        options.AddArgument($"--user-agent={userAgent}");
        
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);

        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddArgument("--start-maximized");
        options.AddArgument("--no-default-browser-check");
        options.AddArgument("--disable-infobars");
        options.AddArgument("--headless");
        // Нужны в Docker-контейнере (non-root, без /dev/shm увеличенного размера)
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        
        _driver = new ChromeDriver(options);
        _driver.ExecuteCdpCommand(
            "Page.addScriptToEvaluateOnNewDocument",
            new Dictionary<string, object?>
            {
                ["source"] = "Object.defineProperty(navigator, 'webdriver', { get: () => undefined });"
            });
    }

    public string GetTextNews(string url)
    {
        _driver.Navigate().GoToUrl(url);
        var newsArticle = _driver.FindElement(By.CssSelector(".ContentPageContainer_content__45nmT"));
        var newsHeadingsAndParagraph = newsArticle.FindElements(By.CssSelector(".Paragraph_paragraph__2FiCb, .Heading_block__zRskh"));
        return string.Join(Environment.NewLine, newsHeadingsAndParagraph.ToList().Select(e => e.Text));
    }
}