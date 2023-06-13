using System;
using CrossPlatformsUserActions.Enums;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreUserActionsWrappers;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Remote;
using static CoreUserActions.Core;
using CoreUserActions.Enums;
using CoreUserActions.AppDataStructs;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Drawing;

namespace S6UserActions
{
    public class WebElementProperty
    {
        public WebElementProperty(IWebElement element)
        {
            TagName = element.TagName;
            Text = element.Text;
            Enabled = element.Enabled;
            Selected = element.Selected;
            Location = new Point(element.Location.X, element.Location.Y);
            Size = new Size(element.Size.Width, element.Size.Height);
            Displayed = element.Displayed;

        }
        public string TagName { get; }
        public string Text { get; }
        public bool Enabled { get; }
        public bool Selected { get; }
        public Point Location { get; }
        public Size Size { get; }
        public bool Displayed { get; }
    }
    public class SeleniumFunctions
    {
        public static IWebDriver driver;
        private static IWebElement area;
        public static Func<UserAction> CREATE_DRIVER_ACTION;

        private const int uiTimeout = 2000;
        /// <summary>
        /// this function  set the timeout to find object in the UI
        /// by default the timeout is 0 - no retry if not found
        /// When this parameter is greater than 0
        /// the function that searches for objects on the screen
        /// will try again and again until you find or until the timeout
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static void CleanDriver()
        {
            driver = null;
        }

        private static object LookupArea
        {
            get
            {
                if (area == null) return driver;
                else return area;
            }
        }

        private static UserAction<IWebElement> FindPathToObject(dynamic lookupScope, string objectIdentifier, int index = 0, string objectTag = "*", int timeout = uiTimeout)
        {
            List<string> objList = new List<string>() { objectIdentifier };
            return FindPathToObject(lookupScope, objList, index, objectTag, timeout);
        }

        private static UserAction<IWebElement> FindPathToObject(dynamic lookupScope, List<string> containersList, int index = 0, string objectTag = "*", int timeout = uiTimeout)
        {
            if (containersList.Count == 0)
            {
                Reporter("objectIdentifiers", "Info", "The list of containers is empty");
                return null;
            }

            foreach (string container in containersList)
            {
                string webElementToSearch = container;
                string action = $"FindPathToObject({webElementToSearch},{index})";
                if (driver == null && CREATE_DRIVER_ACTION != null)
                {
                    var result = CREATE_DRIVER_ACTION();
                    if (result.getResult() == ExecutionResult.Fail || driver == null)
                    {
                        Reporter(action, "Info", "Failed to create selenium driver");
                        return new UserAction<IWebElement>(ExecutionResult.Fail, "FindPathToObject: Failed to create selenium driver");
                    }
                    lookupScope = driver;
                    //Wait for UI Loading animation to complete   
                    ExecutionResult isAnimatingResult = SeleniumFunctions.WaitForObjectAttributeValue(new List<string> { "app_app__" }, "data-isanimating", "false", 30).getResult();
                    if (result.getResult() == ExecutionResult.Fail)
                        return new UserAction<IWebElement>(ExecutionResult.Fail, "", "Failed to find 'data-isanimating' attribute in 'app_app__' class");
                    Thread.Sleep(2000);
                }

                //Checking index mode
                Regex re = new Regex(@"(.*)\[([0-9]*)\]");
                if (re.IsMatch(webElementToSearch))
                {
                    if (int.TryParse(re.Match(webElementToSearch).Groups[2].Value, out index))
                    {
                        webElementToSearch = re.Match(webElementToSearch).Groups[1].Value;
                    }
                }
                IEnumerable<IWebElement> objectsList = FindPathToObjects(lookupScope, webElementToSearch, objectTag);
                if (index >= objectsList.Count())
                {
                    var find = false;
                    var counter = 0;
                    find = SpinWait.SpinUntil(() =>
                    {
                        counter++;
                        Thread.Sleep(200);
                        objectsList = FindPathToObjects(lookupScope, webElementToSearch, objectTag);
                        return index < objectsList.Count();
                    }, timeout);

                    if (!find)
                    {
                        Reporter(action, "Info",
                            $"Could not find object {webElementToSearch} number {index}, found {objectsList.Count()} objects timeout: {uiTimeout} retry {counter} times");
                        return new UserAction<IWebElement>(ExecutionResult.Error,"object not found");
                    }

                }

                //Scroll into view
                if (area == null && driver is ChromeDriver)
                {
                    try
                    {
                        ((ChromeDriver)driver).ExecuteScript("arguments[0].scrollIntoViewIfNeeded();", objectsList.ElementAt(index));
                        Reporter("FindPathToObject", "Info", $"ScrollIntoView succeeded");
                        Thread.Sleep(100);
                    }
                    catch (System.Exception ex)
                    {
                        Reporter("FindPathToObject", "Info", $"ScrollIntoView failed with exception: {ex.Message}");
                    }
                }
                lookupScope = objectsList.ElementAt(index);
                index = 0;

            }
            return new UserAction<IWebElement>(ExecutionResult.Pass, "", lookupScope);
        }
        private static UserAction<IWebElement> FindPathToObjectUnderContainer(List<string> containerList, string elementName, int index = 0, string baseMethodName = "")
        {
            if (baseMethodName == "")
            {
                StackTrace stackTrace = new StackTrace();
                baseMethodName = stackTrace.GetFrame(1).GetMethod().Name;
            }
            var res = FindPathToObject(LookupArea, containerList);
            if (res.getResult() != ExecutionResult.Pass)
            {
                return new UserAction<IWebElement>(ExecutionResult.Fail, $"{baseMethodName}: fail to find container {res.details} {String.Join(",", containerList)} {elementName}");
            }
            HighlightElement(res.getValue(), 5, "red");
            res = FindPathToObject(res.getValue(), elementName);
            if (res.getResult() != ExecutionResult.Pass)
            {
                return new UserAction<IWebElement>(ExecutionResult.Fail, $"{baseMethodName}: fail to find element {res.details} {elementName}");
            }
            return res;
        }
        private static UserAction<IWebElement> FindPathToObjectUnderContainer(string container, string elementName, int index = 0)
        {
            StackTrace stackTrace = new StackTrace();
            List<string> containerList = new List<string>();
            containerList.Add(container);
            return FindPathToObjectUnderContainer(containerList, elementName, index, stackTrace.GetFrame(1).GetMethod().Name);
        }
        public static UserAction GetSelectedValueFromDropDownListUnderContainer(string containerName, string dropDownList)
        {
            try
            {
                var res = FindPathToObjectUnderContainer(containerName, dropDownList);
                IWebElement listElement = res.getValue();

                if (res.getResult() == ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Pass, $"Inner element {dropDownList} was get successfully the selected item is: {listElement.Text}!", listElement.Text);
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, $"GetSelectedValueFromDropDownListUnderContainer {res.details} {containerName} dropDownListname: {dropDownList}");
                }

            }
            catch (System.Exception ex)
            {
                return new UserAction(ExecutionResult.Fail, $"GetSelectedValueFromDropDownListUnderContainer fail {ex.Message} containerName: {containerName} dropDownListname: {dropDownList}");
            }
        }


        /// <summary>
        /// Get elemnt properties
        /// </summary>
        /// <param name="label">Elemnt name</param>
        /// <returns></returns>
        public static UserAction<WebElementProperty> GetElementProperty(string label)
        {
            {
                string action = "GetElementProperty(" + label + ")";
                try
                {
                    IWebElement PathToLabel = FindPathToObject(LookupArea, label).getValue();
                    WebElementProperty webElementProperty = new WebElementProperty(PathToLabel);
                    if (!(PathToLabel == null))
                    {
                        return new UserAction<WebElementProperty>(ExecutionResult.Pass, "elemnt properties were found", webElementProperty);
                    }
                    else
                    {
                        return new UserAction<WebElementProperty>(ExecutionResult.Fail, "Element wasn't found");
                    }

                }
                catch (System.Exception e)
                {
                    return new UserAction<WebElementProperty>(ExecutionResult.Fail, e.Message);
                }
            }
        }

        /// <summary>
        /// Drag and drop an elemnt
        /// </summary>
        /// <param name="strSourceName">Elemnt to drag</param>
        /// <param name="strTargetName">Elemnt to drop on</param>
        /// <returns></returns>
        public static UserAction DragAndDrop(string strSourceName, string strTargetName)
        {
            try
            {
                var resTarget = FindPathToObject(LookupArea, strTargetName);
                var resSource = FindPathToObject(LookupArea, strSourceName);
                if (resSource.getResult() != ExecutionResult.Pass || resTarget.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, $"failed DragAndDrop ({strSourceName},{strTargetName}) {resSource.details} {resTarget.details}");

                }
                Actions action = new Actions(driver);
                action.DragAndDrop(resSource.getValue(), resTarget.getValue()).Perform();
                return new UserAction(ExecutionResult.Pass, $"DragAndDrop ({strSourceName},{strTargetName}) was done successfully");
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, $"failed DragAndDrop ({strSourceName},{strTargetName}) {e.Message}");
            }

        }
        /// <summary>
        /// Drag and drop an elemnt
        /// </summary>
        /// <param name="strSourceName">Elemnt to drag</param>
        /// <param name="xOffset ">num of x pixels to move in X axis</param>
        /// <param name="yOffset">num of x pixels to move in Y axis</param>
        /// <returns></returns>
        public static UserAction SwipeObject(string strSourceName, int xOffset, int yOffset)
        {
            try
            {
                var resSource = FindPathToObject(LookupArea, strSourceName);
                if (resSource.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, $"failed DragAndDrop ({strSourceName},) {resSource.details}");

                }
                Actions action = new Actions(driver);
                action.DragAndDropToOffset(resSource.getValue(), xOffset, yOffset).Perform();
                return new UserAction(ExecutionResult.Pass, $"DragAndDrop ({strSourceName},{xOffset}{yOffset}) was done successfully");
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, $"failed DragAndDrop ({strSourceName},{xOffset}{yOffset}) {e.Message}");
            }

        }
        public static UserAction SelectFromDropDownListUnderContainer(string containerName, string dropDownList, string valueToSelect)
        {
            try
            {
                var res = FindPathToObjectUnderContainer(containerName, dropDownList);

                if (res.getResult() == ExecutionResult.Pass)
                {
                    var listElement = res.getValue();
                    HighlightElement(listElement, 5);
                    try
                    {
                        //Find button under dropDownList container and click it, if button not found click on dropDownList element
                        IWebElement expandButton = listElement.FindElement(By.TagName("Button"));
                        expandButton.Click();
                    }
                    catch (NoSuchElementException)
                    {
                        listElement.Click();
                    }

                    try
                    {
                        //Find "valueToSelect" text inside containerName class and click the value
                        var PathToContainer = FindPathToObject(LookupArea, containerName).getValue();
                        IWebElement innerTextToSelect = PathToContainer.FindElement(By.XPath($"//*[@class='{PathToContainer.GetAttribute("class")}']//*[contains(text(),'{valueToSelect}')]"));
                        innerTextToSelect.Click();
                        return new UserAction(ExecutionResult.Pass, $"inner element {valueToSelect} was selected successfully!");
                    }
                    catch (NoSuchElementException)
                    {
                        return new UserAction(ExecutionResult.Fail, $"drop down item {valueToSelect} wasn't found on drop down menu!");
                    }
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }

            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, "Exception has been thrown:" + e.Message);
            }
        }

        public static UserAction<List<string>> GetDropDownListValues(string containerName, string dropDownList)
        {
            List<string> dropDownOptions = new List<string>();
            try
            {
                var res = FindPathToObjectUnderContainer(containerName, dropDownList);
                if (res.getResult() == ExecutionResult.Pass)
                {
                    try
                    {
                        var listElement = res.getValue();
                        var options = listElement.FindElements(By.TagName("option"));
                        foreach (var op in options)
                        {
                            dropDownOptions.Add(op.GetAttribute("value"));
                        }
                        if (dropDownList.Count() > 0)
                            return new UserAction<List<string>>(ExecutionResult.Pass, $" {dropDownList.Count()} drop down options were found!", dropDownOptions);
                        else
                            return new UserAction<List<string>>(ExecutionResult.Fail, $"No drop down options were found in {dropDownList}");
                    }
                    catch (NoSuchElementException e)
                    {
                        return new UserAction<List<string>>(ExecutionResult.Fail, $"Drop down item  wasn't found in {dropDownList}!,Exception has been thrown: {e.Message}");
                    }
                }
                else
                {
                    return new UserAction<List<string>>(ExecutionResult.Fail, $"Drop down {dropDownList}  wasn't found in container ");
                }


            }
            catch (System.Exception e)
            {
                return new UserAction<List<string>>(ExecutionResult.Fail, "Exception has been thrown:" + e.Message);
            }
        }


        public static UserAction HoverElementUnderContainerAndGetTooltipValue(string container, string innerElementToViewTheTooltip)
        {
            string action = "HoverElementUnderContainerAndGetTooltipValue(" + container + "," + innerElementToViewTheTooltip + ")";
            try
            {
                var res = FindPathToObjectUnderContainer(container, innerElementToViewTheTooltip);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement innerElement = res.getValue();

                Actions builder = new Actions(driver);
                builder.MoveToElement(innerElement).Build().Perform();

                res = FindPathToObjectUnderContainer(container, "ngb-tooltip");


                IWebElement tooltipElement = res.getValue();
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }

                var strValue = tooltipElement.Text;

                return new UserAction(ExecutionResult.Pass, $"inner element {tooltipElement} was Get successfully {strValue} !", strValue);

            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, "Exception has been thrown:" + e.Message);
            }
        }
        public static UserAction FocusOnScreen(string screenName)
        {
            if (screenName == "none")
            {
                area = null;
                return new UserAction(ExecutionResult.Pass, "No focus mode");
            }

            UserAction<IWebElement> res;
            if (screenName == "MainController")
                res = FindPathToObjectUnderContainer("app-page", "app_headerWithMainController__");
            else if (screenName == "planner")
                res = FindPathToObjectUnderContainer("app-page", "print-queue_container__");
            else if (screenName == "monitor")
                res = FindPathToObjectUnderContainer("app-page", "monitor_monitorContainer__");
            else
                return new UserAction(ExecutionResult.Fail, $"Screen name: {screenName} not a valid name");

            if (res.getResult() != ExecutionResult.Pass)
            {
                return new UserAction(ExecutionResult.Fail, res.details);
            }

            //Scroll into view
            if (driver is ChromeDriver)
            {
                try
                {
                    ((ChromeDriver)driver).ExecuteScript("arguments[0].scrollIntoView(true);", area);
                    Reporter("FindPathToObject", "Info", $"ScrollIntoView succeeded");
                    Thread.Sleep(100);
                }
                catch (System.Exception ex)
                {
                    Reporter("FindPathToObject", "Info", $"ScrollIntoView failed with exception: {ex.Message}");
                }
            }
            return new UserAction(ExecutionResult.Pass, $"Page set to: {screenName}");
        }
        public static UserAction FocusOnMonitor(string monitorName)
        {
            string action = $"FocusOnMonitor({monitorName})";
            if (driver == null && CREATE_DRIVER_ACTION != null)
            {
                var result = CREATE_DRIVER_ACTION();
                if (result.getResult() == ExecutionResult.Fail || driver == null)
                {
                    Reporter(action, "Info", "Failed to create selenium driver");
                    return new UserAction(ExecutionResult.Fail, "FindPathToObject: Failed to create selenium driver");
                }
            }
            bool isWindowFound = false;
            foreach (var handle in driver.WindowHandles)
            {
                driver.SwitchTo().Window(handle);
                if (driver.Title.Contains(monitorName))
                {
                    isWindowFound = true;
                    break;
                } 
            }
            if (!isWindowFound)
            {
                return new UserAction(ExecutionResult.Fail, $"Screen name: {monitorName} not a valid name");
            }

            //The windows opened minmized by default and not rendered, therefore the objects not interactable.
            //We have to use ShowWindow to render the UI.
            IntPtr intPtr = CrossPlatformsUserActions.CrossPlatforms.FindWindow(null, monitorName);
            ShowWindow(intPtr, (int)CoreUserActions.Core.ShowWindowCommands.ShowMaximized);
            return new UserAction(ExecutionResult.Pass, $"Focus set to: {monitorName} monitor");
        }
        private static IEnumerable<IWebElement> FindPathToObjects(dynamic lookupScope, string objectIdentifier, string objectTag = "*")
        {
            string action = $"FindPathToObjects({objectIdentifier})";
            Reporter(action, "Info", $"Looking for the object {objectIdentifier}");
            IEnumerable<IWebElement> objectsList = lookupScope.FindElements(By.XPath($@".//{objectTag}[text()=""{objectIdentifier}"" or @id=""{objectIdentifier}"" or name()=""{objectIdentifier}"" or @data-testId=""{objectIdentifier}"" or @data-testid=""{objectIdentifier}"" or @class=""{objectIdentifier}""]"));
            Reporter(action, "Info", $"{objectsList.Count()} exact matches found");
            if (objectsList.Count() == 0)
            {
                if (objectIdentifier.IndexOf(".*") > -1)
                {
                    //Support .* at the beginning/middle/end of string
                    string[] identifierParts = objectIdentifier.Split(new string[] { ".*" }, StringSplitOptions.RemoveEmptyEntries);
                    if (identifierParts.Count() < 2)
                    {
                        //in case we want to find object that starts with specific prefix
                        objectsList = lookupScope.FindElements(By.XPath($".//{objectTag}[starts-with(text(), '{identifierParts[0]}') or starts-with(@id, '{ identifierParts[0] }') or starts-with(name(), '{ identifierParts[0] }') or starts-with(@class, '{ identifierParts[0] }')]"));
                    }
                    else
                    {
                        objectsList = lookupScope.FindElements(By.XPath($".//{objectTag}[(starts-with(text(), '{identifierParts[0]}') and contains(text(), '{identifierParts[1]}')) or (starts-with(@id, '{ identifierParts[0] }') and contains(@id, '{ identifierParts[1] }')) or (starts-with(name(), '{ identifierParts[0] }') and contains(name(), '{ identifierParts[1] }')) or (starts-with(@class, '{ identifierParts[0] }') and contains(@class, '{ identifierParts[1] }'))]"));
                    }
                    Reporter(action, "Info", $"object identifier was containing .* - replaced with blank, new value:{objectIdentifier}");
                }
                else
                {
                    //Check for contained string
                    //objectsList = lookupScope.FindElements(By.XPath($".//{objectTag}[contains(text(), '{ objectIdentifier }') or contains(@id, '{ objectIdentifier }') or contains(name(),'{ objectIdentifier }') or contains(@class,'{ objectIdentifier }')]"));
                    objectsList = lookupScope.FindElements(By.XPath($".//{objectTag}[starts-with(text(), '{objectIdentifier}') or starts-with(@id, '{ objectIdentifier }') or starts-with(name(), '{ objectIdentifier }') or starts-with(@class, '{ objectIdentifier }')]"));
                    if (objectsList.Count() == 0)
                    {
                        objectsList = lookupScope.FindElements(By.XPath($".//{objectTag}[contains(text(), '{ objectIdentifier }') or contains(@id, '{ objectIdentifier }') or contains(name(),'{ objectIdentifier }') or contains(@class,'{ objectIdentifier }')]"));
                    }
                }
                Reporter(action, "Info", $"{objectsList.Count()} partial match objects found!");
            }
            foreach (IWebElement element in objectsList)
            {
                HighlightElement(element, 5);
            }

            return objectsList;

            // TODO additions:
            //If no objects found - Find all elements where id\text\name contains object name
            //If found more than 1 object - narrow the selection by object type(tagname)
            //If found more than 1 object and object isDisplayed is not null and there's no index defined - find the top one
        }
        public static UserAction<ExistenceStatus> waitForObjectExistence(string objName, int timeout)
        {
            try
            {
                IWebElement element = FindPathToObject(LookupArea, objName, timeout: timeout).getValue();
                ExistenceStatus actExistence = element == null ? ExistenceStatus.not_exist : ExistenceStatus.exist;
                return new UserAction<ExistenceStatus>(ExecutionResult.Pass, $"Existence status is: {actExistence}", actExistence);
            }
            catch (System.Exception e)
            {
                return new UserAction<ExistenceStatus>(ExecutionResult.Fail, $"Exception has been thrown: {e.Message}", ExistenceStatus.error);
            }
        }
        public static UserAction<ExistenceStatus> GetObjectExistence(List<string> containersList)
        {
            try
            {
                var res = FindPathToObject(LookupArea, containersList);
                if (res.getResult() == ExecutionResult.Pass)
                {
                    return new UserAction<ExistenceStatus>(ExecutionResult.Pass, res.details, ExistenceStatus.exist);
                }
                if (res.getResult() == ExecutionResult.Error)
                {
                    return new UserAction<ExistenceStatus>(ExecutionResult.Pass, res.details, ExistenceStatus.not_exist);

                }
                return new UserAction<ExistenceStatus>(ExecutionResult.Fail, res.details, ExistenceStatus.error);
            }
            catch (System.Exception e)
            {
                return new UserAction<ExistenceStatus>(ExecutionResult.Fail, $"Exception has been thrown: {e.Message}", ExistenceStatus.error);
            }
        }


        /// <summary>
        /// Return the times of sn object occurrence in the curren UI layout
        /// </summary>
        /// <param name="elementName"></param>
        /// <param name="objectTag"></param>
        /// <returns></returns>
        public static UserAction<int>GetObjectOccurrenceCount(string elementName,string objectTag="*")
        {
            try
            {
                IEnumerable<IWebElement> objectsList = FindPathToObjects(LookupArea, elementName, objectTag);
                return new UserAction<int>(ExecutionResult.Pass, $"GetObjectOccurrenceCount returned  {objectsList.Count()}", objectsList.Count());
            }
            catch(System.Exception e)
            {
                return new UserAction<int>(ExecutionResult.Fail, $"Exception has been thrown: {e.Message}", "");
            }
        }
        /// <summary>
        /// Reurns required attribute value for given object
        /// </summary>
        /// <param name="containersList"></param>
        /// <param name="attribute"></param>
        /// <returns></returns>
        public static UserAction<string > GetObjectAttribute(List<string> containersList, string attribute)
        {
            try
            {
                (IWebElement element,bool hasValue) = FindPathToObject(LookupArea, containersList).tryGetValue<IWebElement>();
                if (hasValue)
                {
                    string attributeValue = element.GetAttribute(attribute);
                    return new UserAction<string>(ExecutionResult.Pass, $"pass", attributeValue);
                }
                else
                {
                    return new UserAction<string>(ExecutionResult.Fail, $"Element was not found");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction<string>(ExecutionResult.Fail, $"Exception has been thrown: {e.Message}", "");
            }
        }
        public static UserAction<ExistenceStatus> VerifyObjectExistenceUnderContainer(List<string> containersList)
        {
            try
            {
                var res = FindPathToObject(LookupArea, containersList);
                if (res.getResult() == ExecutionResult.Pass)
                {
                    return new UserAction<ExistenceStatus>(ExecutionResult.Pass, res.details, ExistenceStatus.exist);
                }
                if (res.getResult() == ExecutionResult.Error)
                {
                    return new UserAction<ExistenceStatus>(ExecutionResult.Pass, res.details, ExistenceStatus.not_exist);
                }

                    return new UserAction<ExistenceStatus>(ExecutionResult.Fail, res.details, ExistenceStatus.error);
                
             }
            catch (System.Exception e)
            {
                return new UserAction<ExistenceStatus>(ExecutionResult.Fail, $"Exception has been thrown: {e.Message}", ExistenceStatus.error);
            }
        }
        public static UserAction WaitForObjectExistenceUnderContainer(List<string> containersList, ExistenceStatus existence,
            int timeoutInSeconds)
        {
            try
            {
                bool find = SpinWait.SpinUntil(() =>
                {
                    var element = FindPathToObject(LookupArea, containersList);
                    ExistenceStatus existenceStatus = element.value == null ? ExistenceStatus.not_exist : ExistenceStatus.exist;
                    return existenceStatus == existence;
                }, TimeSpan.FromSeconds(timeoutInSeconds));

                if (!find)
                {
                    return new UserAction(ExecutionResult.Fail, $"Object existence status is not as expected: {existence}");
                }
                return new UserAction(ExecutionResult.Pass, $"Object existence status is: {existence}");
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, $"Exception has been thrown: {e.Message}");
            }
        }
        public static UserAction VerifyObjectAttributeValue(List<string> objContainersList, string attributeName, string expectedValue)
        {
            var res = FindPathToObject(LookupArea, objContainersList);
            if (res.getResult() != ExecutionResult.Pass)
            {
                return new UserAction(ExecutionResult.Fail, res.details);
            }
            IWebElement webElement = res.getValue();
            if (webElement == null)
            {
                if (objContainersList.Count == 1)
                {
                    return new UserAction(ExecutionResult.Fail, $"The object ''{objContainersList[0]}'' not found");
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, $"The object not found");
                }
            }

            string attributeValue = webElement.GetAttribute(attributeName);
            if (attributeValue == null)
            {
                return new UserAction(ExecutionResult.Fail, $"The attribute ''{attributeName}'' not found");
            }
            else if (attributeValue.ToLower() == expectedValue.ToLower())
            {
                return new UserAction(ExecutionResult.Pass, $"The attribute ''{attributeName}'' value is: ''{attributeValue}'' as expected", attributeValue);
            }
            else
            {
                return new UserAction(ExecutionResult.Fail, $"The attribute ''{attributeName}'' value is: ''{attributeValue}'', expected: ''{expectedValue}''");
            }
        }

        public static UserAction WaitForObjectAttributeValue(List<string> objContainersList, string attributeName, string expectedValue, int timeoutInSec)
        {
            string valResult = expectedValue;
            try
            {
                bool find = SpinWait.SpinUntil(() =>
                {
                    UserAction result = null;
                    try
                    {
                        result = VerifyObjectAttributeValue(objContainersList, attributeName, expectedValue);
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                    if (result.getResult().Equals(ExecutionResult.Pass))
                    {
                        valResult = result.getValue();
                        return true;
                    }
                    return false;

                }, TimeSpan.FromSeconds(timeoutInSec));

                if (!find)
                {
                    return new UserAction(ExecutionResult.Fail, $"Object existence status is not as expected: {expectedValue}");
                }
                return new UserAction(ExecutionResult.Pass, $"Object existence status is: {valResult}", valResult);
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, $"Exception has been thrown: {e.Message}");
            }
        }

        public static UserAction VerifyFieldStatus(List<string> objContainersList, string expectedStatus)
        {
            try
            {
                var res = FindPathToObject(LookupArea, objContainersList);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement pathToField = res.getValue();

                string objectName = objContainersList[objContainersList.Count - 1];
                if ((pathToField.Enabled) && (expectedStatus == "enabled"))
                {
                    return new UserAction(ExecutionResult.Pass, $"The field: {objectName} is as expected status: {expectedStatus}");
                }
                else if ((pathToField.Enabled == false) && (expectedStatus == "disabled"))
                {
                    return new UserAction(ExecutionResult.Pass, $"The field: {objectName} is  as expected status: {expectedStatus}");
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, $"The field: {objectName} is not as expected status: {expectedStatus}");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, e.Message);
            }
        }

        public static UserAction VerifyButtonSelection(List<string> objContainersList, string expectedStatus)
        {
            try
            {
                var res = FindPathToObject(LookupArea, objContainersList);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement pathToRadioButton = res.getValue();

                string objectName = objContainersList[objContainersList.Count - 1];
                if ((pathToRadioButton.Selected) && ((expectedStatus == "selected") || (expectedStatus == "ON")))
                {
                    return new UserAction(ExecutionResult.Pass, $"The object: {objectName} is as expected status: {expectedStatus}");
                }
                else if ((pathToRadioButton.Selected == false) && ((expectedStatus == "unselected") || (expectedStatus == "NO")))
                {
                    return new UserAction(ExecutionResult.Pass, $"The object: {objectName} is  as expected status: {expectedStatus}");
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, $"The object: {objectName} is not as expected status: {expectedStatus}");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, e.Message);
            }
        }

        public static UserAction<SelectionStatus> GetRadioButtonSelection(List<string> objContainersList)
        {
            try
            {
                var res = FindPathToObject(LookupArea, objContainersList);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction<SelectionStatus>(ExecutionResult.Fail, res.details);
                }
                IWebElement pathToRadioButton = res.getValue();
                if (pathToRadioButton == null)
                {
                    return new UserAction<SelectionStatus>(ExecutionResult.Fail, "The object not found");
                }
                SelectionStatus actSelection = pathToRadioButton.Selected == true ? SelectionStatus.selected : SelectionStatus.unselected;
                return new UserAction<SelectionStatus>(ExecutionResult.Pass, $"Selection button is: {actSelection}", actSelection);
            }
            catch (System.Exception e)
            {
                return new UserAction<SelectionStatus>(ExecutionResult.Fail, $"Exception has been thrown: {e.Message}", SelectionStatus.error);
            }
        }
        public static UserAction VerifyIconStatus(string iconName, string innerName, string expAttribute, string existence = "exist")
        {
            var res = FindPathToObjectUnderContainer(iconName, innerName);
            if (res.getResult() != ExecutionResult.Pass)
            {
                return new UserAction(ExecutionResult.Fail, res.details);
            }
            var status = res.getValue();

            string classValue = status.GetAttribute("class").ToLower();
            List<string> attributes = expAttribute.ToLower().Split(',').ToList();
            bool contains = attributes.All(attribute => classValue.Contains(attribute.ToString().ToLower()));
            if ((contains && existence == "exist") || (!contains && existence == "not_exist"))
            {
                return new UserAction(ExecutionResult.Pass, $"Attribute {expAttribute} {existence} under {innerName} under {iconName}, as expected");
            }
            else
            {
                return new UserAction(ExecutionResult.Warning, $"Attribute {expAttribute} {existence} under {innerName} under {iconName}, not as expected. actual content: {classValue}");
            }

        }
        private static bool HighlightElement(IWebElement element, int highlightTimeMS, string color = "red")
        {
            try
            {
                string currentStyleDefinitions = element.GetAttribute("style");
                List<string> styleArray = currentStyleDefinitions.Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                int borderPropertyIndex = -1;
                borderPropertyIndex = styleArray.FindIndex(x => x.StartsWith("border:"));

                IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
                //string title = (string)js.ExecuteScript("return document.title");

                DateTime startTime = DateTime.Now;
                DateTime endTime = startTime + new TimeSpan(0, 0, 0, 0, highlightTimeMS);
                while (endTime > DateTime.Now)
                {
                    if (borderPropertyIndex == -1)
                    {
                        styleArray.Add($"border: solid 2px {color};");
                        int latestStyleIndex = styleArray.Count() - 1;
                        js.ExecuteScript($"arguments[0].setAttribute('style','{ String.Join(";", styleArray)}');", element);
                        Thread.Sleep(100);
                        styleArray[latestStyleIndex] = "border: transparent;";
                        js.ExecuteScript($"arguments[0].setAttribute('style','{ String.Join(";", styleArray)}');", element);
                        Thread.Sleep(100);
                    }
                }
                return true;
            }
            catch (System.Exception ex)
            {
                Reporter("Highlight", "Info", $"Highlight threw an exception:{ex.Message}");
                return false;
            }
        }

        public static void appCloseHandler(CrossPlatformsUserActions.CtrlType sig = CrossPlatformsUserActions.CtrlType.NONE)
        {
            if (driver != null)
            {
                if (sig != CrossPlatformsUserActions.CtrlType.NONE || CrossPlatformsUserActions.CrossPlatforms.isExceptionRaised)
                {
                    if (sig == CrossPlatformsUserActions.CtrlType.CTRL_CLOSE_EVENT)
                        Console.WriteLine("---X button was clicked---");
                    else
                        Console.WriteLine("---Shutdown event or exception recived----");
                }
                else if (SessionConfigurations.executionMode == SessionConfigurations.ExecutionMode.Debug)
                {
                    Console.BackgroundColor = ConsoleColor.Blue;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.WriteLine("Enter Y to end the execution, pay attention closing the console will close the UI.");
                    while (!String.Equals(Console.ReadKey().Key.ToString(), "y", StringComparison.OrdinalIgnoreCase)) ;
                }
                Console.WriteLine("---Closing the console---");
                CloseBrowser();
            }
            CrossPlatformsUserActions.CrossPlatforms._closingAppsHandler -= appCloseHandler;
        }
        public static UserAction OpenBrowser(string URL, string browserType)
        {
            string action = "OpenBrowser(" + URL + "," + browserType + ")";
            int timeout = 5;
            try
            {
                BrowserTypes browser;
                if (!Enum.TryParse(browserType, out browser))
                {
                    throw new System.Exception($"Browser type {browserType} not supported!");
                }

                driver = DriverFactory.CreateDriver(browser);
                try
                {
                    driver.Manage().Window.Maximize();
                }
                catch (System.Exception)
                {
                    Reporter(action, "Info", "Failed To Maximize browser window");
                }
                driver.Url = URL;
                Thread.Sleep(2000);
                return new UserAction(ExecutionResult.Pass, "Browser opened");
            }
            catch (System.Exception ex)
            {
                return new UserAction(ExecutionResult.Fail, "Open Browser Failed, exception: " + ex.Message);
            }
        }
        public static UserAction OpenBrowser(string appPath)
        {
            try
            {
                //Kill precious instances of the application
                Process[] p = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(appPath));
                Array.ForEach(p, (item) => { item.Kill(); });
                //Create new instance of the WebDriver
                driver = DriverFactory.CreateDriver(appPath);
                CrossPlatformsUserActions.CrossPlatforms._closingAppsHandler += appCloseHandler;
                Thread.Sleep(3000);

                foreach (var handle in driver.WindowHandles)
                {
                    driver.SwitchTo().Window(handle);
                    if (driver.Title.Contains("Operator"))
                        break;
                }

                List<string> electronScreens = new List<string> { "PrintingEngine", "PrimingAndWebHandlingSystem", "Operator" };
                foreach (var screen in electronScreens)
                {
                    IntPtr intPtr = CrossPlatformsUserActions.CrossPlatforms.FindWindow(null, screen);
                    ShowWindow(intPtr, (int)CoreUserActions.Core.ShowWindowCommands.ShowMaximized);
                }
                return new UserAction(ExecutionResult.Pass, "Electron application opened");
            }
            catch (System.Exception ex)
            {
                return new UserAction(ExecutionResult.Fail, "Failed to open electron, exception: " + ex.Message);
            }
        }
        public static UserAction CloseBrowser()
        {
            string action = "CloseBrowser()";
            try
            {
                if (driver != null)
                {
                    driver.Quit();
                    driver.Dispose();
                }
                else
                {
                    Reporter(action, "Info", "Driver object is nothing");
                }
                return new UserAction(ExecutionResult.Pass, "Browser closed");
            }
            catch (System.Exception ex)
            {
                return new UserAction(ExecutionResult.Fail, ex.Message);
            }
        }
        public static UserAction RefreshPage()
        {
            string action = "RefreshPage()";
            try
            {
                driver.Navigate().Refresh();
                return new UserAction(ExecutionResult.Pass, "Page refreshed");
            }
            catch (System.Exception ex)
            {
                return new UserAction(ExecutionResult.Fail, ex.Message);
            }
        }
        /// <summary>
        /// Click and hold button
        /// </summary>
        /// <param name="strButtonName"></param>
        /// <returns></returns>
        public static UserAction ClickAndHoldButton(string strButtonName)
        {
            string action = "ClickAndHoldButton(" + strButtonName + ")";
            try
            {
                var res = FindPathToObject(LookupArea, strButtonName);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement PathToButton = res.getValue();

                if (PathToButton.Enabled && !(PathToButton.GetAttribute("class").EndsWith("_dis")))
                {
                    Actions actions = new Actions(driver);
                    actions.ClickAndHold(PathToButton).Build().Perform();
                    return new UserAction(ExecutionResult.Pass, "Button was clicked");
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, "Button is disabled");
                }

            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, e.Message);
            }
        }

        public static UserAction ClickOnButton(string strButtonName)
        {
            string action = "ClickOnButton(" + strButtonName + ")";
            try
            {
                var res = FindPathToObject(LookupArea, strButtonName);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement PathToButton = res.getValue();
                if (!(PathToButton == null))
                {
                    if (PathToButton.Enabled)
                    {
                        PathToButton.Click();
                        return new UserAction(ExecutionResult.Pass, "Button was clicked");
                    }
                    else
                    {
                        return new UserAction(ExecutionResult.Fail, "Button is disabled");
                    }
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, "Button not found");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, e.Message);
            }
        }

        /// <summary>
        /// The function getting Buzzerable button name, searching for BuzzerNeeded string in the child classes
        /// Then pressing on the button, checking that BuzzerNeeded changes to Buzzing after 1 second and waiting for BuzzerNotNeeded
        /// With timeout of 10 seconds, when the class name changes to  BuzzerNotNeeded we pressing on the button once again.
        /// </summary>
        /// <param name="strButtonName">Button name (ID/Class/data-testid from HTML)</param>
        public static UserAction ClickOnBuzzerableButton(string strButtonName)
        {
            try
            {
                //when object has data-buzzing attribute
                var res = FindPathToObject(LookupArea, strButtonName);

                if (res.getResult() != ExecutionResult.Pass)
                    return new UserAction(ExecutionResult.Fail, res.details);

                IWebElement buttonElement = res.getValue();
                if (buttonElement == null)
                    return new UserAction(ExecutionResult.Fail, $"Buzzerable button '{strButtonName}' is null!");

                ExecutionResult buzzerNeeded = WaitForObjectAttributeValue(new List<string> { strButtonName }, "data-buzzing", "Needed", 5).getResult();
                if (buzzerNeeded != ExecutionResult.Pass)
                    return new UserAction(ExecutionResult.Fail, $" '{strButtonName}' is not Buzzerable button");

                buttonElement.Click();
                Thread.Sleep(500);
                DateTime startTime = DateTime.Now;
                do
                {
                    if (buttonElement.GetAttribute("data-buzzing").Equals("NotNeeded"))
                    {
                        buttonElement.Click();
                        return new UserAction(ExecutionResult.Pass, $"Buzzerable button '{strButtonName}' was clicked sucesfully");
                    }
                    Thread.Sleep(50);
                } while (DateTime.Now.Subtract(startTime).TotalSeconds <= 10);

                return new UserAction(ExecutionResult.Fail, $"Buzzer status for buzzerable button '{strButtonName}' not changed to 'NotNeeded'");
            }
            catch (System.Exception ex)
            {
                return new UserAction(ExecutionResult.Fail, ex.Message);
            }
        }

        /// <summary>
        /// the function open the menu and wait until the menu open or timeout
        /// and return the item 
        /// </summary>
        /// <param name="menuName">menuName</param>
        /// <param name="itemName">itemName</param>
        /// <returns>the item under menu</returns>
        public static UserAction<IWebElement> GetItemfromMenu(string menuName, string itemName)
        {
            List<string> itemMenuContainers = new List<string> { menuName };
            List<string> itemNameContainers = new List<string> { itemName };
            if (GetObjectExistence(itemMenuContainers).getValue() == ExistenceStatus.exist)
            {
                ClickOnButton(menuName);
                if (GetObjectExistence(itemNameContainers).getValue() != ExistenceStatus.exist)
                {
                    return new UserAction<IWebElement>(ExecutionResult.Fail, "fail to get item under menu , timeout");
                }
                else
                {
                    return new UserAction<IWebElement>(ExecutionResult.Pass, "find item under menu", FindPathToObject(driver, itemName).getValue());
                }
            }
            else
            {
                return new UserAction<IWebElement>(ExecutionResult.Fail, "fail to get menu button , timeout");
            }
        }
        public static UserAction<List<string>> GetListItems(string listName)
        {
            string action = "GetListItems(" + listName + ")";
            try
            {
                var res = FindPathToObject(LookupArea, listName);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction<List<string>>(ExecutionResult.Fail, res.details);
                }
                IWebElement PathToList = res.getValue();
                if (!(PathToList == null))
                {
                    HighlightElement(PathToList, 5, "red");
                    IEnumerable<IWebElement> listItems = FindPathToObjects(PathToList, "li");
                    List<string> listItemsText = new List<string>();
                    foreach (IWebElement listItem in listItems)
                    {
                        listItemsText.Add(listItem.Text);
                    }
                    return new UserAction<List<string>>(ExecutionResult.Pass, $"found {listItems.Count()} list items", listItemsText);
                }
                else
                {
                    return new UserAction<List<string>>(ExecutionResult.Fail, "List wasn't found");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction<List<string>>(ExecutionResult.Fail, e.Message);
            }
        }
        public static UserAction ClickOnContainedElement(string container, string innerElementToClick, string indexElement, string duration, string numberOfClicks)
        {
            try
            {
                int.TryParse(indexElement, out var index);
                var res = FindPathToObjectUnderContainer(container, innerElementToClick, index);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }

                var innerElement = res.getValue();
                HighlightElement(innerElement, 5);
                if (duration == "0")
                {
                    for (int i = 0; i < int.Parse(numberOfClicks); i++)
                    {
                        innerElement.Click();
                    } 
                    return new UserAction(ExecutionResult.Pass, $"inner element {innerElementToClick} was clicked successfully!");
                }
                else
                {
                    Actions action1 = new Actions(driver);
                    action1.MoveToElement(innerElement).ClickAndHold().Build().Perform();
                    Thread.Sleep(int.Parse(duration) * 1000);
                    return new UserAction(ExecutionResult.Pass, $"inner element {innerElementToClick} was clicked for {duration} seconds");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, "Exception has been thrown:" + e.Message);
            }
        }
        /// <summary>
        /// Verifies all object names in list are referring to the same object 
        /// </summary>
        /// <param name="objectNames"></param>
        /// <returns></returns>
        public static UserAction VerifyObjectsEquality(List<string> objectNames)
        {
            var res = FindPathToObject(LookupArea, objectNames[0]);
            if (res.getResult() != ExecutionResult.Pass)
            {
                return new UserAction(ExecutionResult.Fail, res.details);
            }
            IWebElement element = res.getValue();
            if (element == null)
                return new UserAction(ExecutionResult.Fail, $"{objectNames[0]} Object wasn't found");
            string id = element.ToString();
            for (int i = 1; i < objectNames.Count; i++)
            {
                element = FindPathToObject(LookupArea, objectNames[i]).getValue();
                if (id != element.ToString())
                    return new UserAction(ExecutionResult.Fail, $"{objectNames[i]} is not referring to the {objectNames[0]} object");
                else
                    id = element.ToString();
            }

            return new UserAction(ExecutionResult.Pass, "Objects are equals");
        }

        public static UserAction<object> GetFieldPropertyUnderContainer(string container, string innerElementToGet, string FieldName)
        {
            string action = "GetFieldPropertyUnderContainer(" + container + "," + innerElementToGet + ")";
            try
            {
                var res = FindPathToObjectUnderContainer(container, innerElementToGet);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction<object>(ExecutionResult.Fail, res.details);
                }

                IWebElement innerElement = res.getValue();

                object value = null;
                switch (FieldName)
                {
                    case "Enabled":
                        value = innerElement.Enabled;
                        break;
                    case "Displayed":
                        value = innerElement.Displayed;
                        break;
                    default:
                        value = innerElement;
                        break;
                }

                return new UserAction<object>(ExecutionResult.Pass, $"inner element {innerElementToGet} was Get successfully  !", value);


            }
            catch (System.Exception e)
            {
                return new UserAction<object>(ExecutionResult.Fail, "Exception has been thrown:" + e.Message);
            }
        }
        public static UserAction GetFieldValueUnderContainer(string container, string innerElementToGet)
        {
            string action = "GetFieldValueUnderContainer(" + container + "," + innerElementToGet + ")";
            try
            {
                var res = FindPathToObjectUnderContainer(container, innerElementToGet);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }

                IWebElement innerElement = res.getValue();


                var strValue = innerElement.GetAttribute("value");

                return new UserAction(ExecutionResult.Pass, $"inner element {innerElementToGet} was Get successfully {strValue} !", strValue);
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, "Exception has been thrown:" + e.Message);
            }
        }

        public static UserAction GetLabelValue(List<string> containers)
        {
            try
            {
                var res = FindPathToObject(LookupArea, containers);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement innerElement = res.getValue();


                var strValue = innerElement.Text;

                return new UserAction(ExecutionResult.Pass, $"Label {containers} value is:{strValue} !", strValue);

            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, "Exception has been thrown:" + e.Message);
            }
        }
        public static UserAction SetFieldValueUnderContainer(string container, string innerElementToSet, string strValue, string index)
        {
            string action = "SetFieldValueUnderContainer(" + container + "," + innerElementToSet + ")";
            try
            {
                int.TryParse(index, out var result);
                var res = FindPathToObjectUnderContainer(container, innerElementToSet);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement innerElement = res.getValue();

                SpinWait.SpinUntil(() =>
                {
                    innerElement.SendKeys(Keys.Control + "a" + Keys.Backspace);
                    return (innerElement.GetAttribute("value") == "");

                }, 1000);
                HighlightElement(innerElement, 5);
                innerElement.SendKeys(strValue);
                if (innerElement.GetAttribute("value") == strValue || Convert.ToInt32(innerElement.GetAttribute("value")) == Convert.ToInt32(strValue))
                {
                    return new UserAction(ExecutionResult.Pass, $"inner element {innerElementToSet} was Set successfully to {strValue}!");
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, $"The value was not set correctly - expected: {strValue}, actual: {innerElement.Text}");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, "Exception has been thrown:" + e.Message);
            }
        }
        public static UserAction<List<string>> GetElementContainedTexts(string elementName)
        {
            string action = "GetElementContainedTexts(" + elementName + ")";
            try
            {
                var res = FindPathToObject(LookupArea, elementName);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction<List<string>>(ExecutionResult.Fail, res.details);
                }
                IWebElement PathToElement = res.getValue();

                HighlightElement(PathToElement, 5, "blue");
                List<IWebElement> elementsWithTextList = PathToElement.FindElements(By.XPath($".//*[string-length(text()) > 0]")).ToList<IWebElement>();
                Reporter(action, "Info", $"Found {elementsWithTextList.Count()} elements with text");
                List<string> textCollection = new List<string>();
                foreach (IWebElement element in elementsWithTextList)
                {
                    textCollection.Add(element.Text);
                }
                return new UserAction<List<string>>(ExecutionResult.Pass, $"found {elementsWithTextList.Count()} elements with text", textCollection);
            }
            catch (System.Exception e)
            {
                return new UserAction<List<string>>(ExecutionResult.Fail, e.Message);
            }
        }

        internal static UserAction SelectRadioButton(string strButtonName)
        {
            throw new NotImplementedException();
        }

        internal static UserAction VerifyRadiobuttonSelected(string strButtonName, string strExpValue)
        {
            throw new NotImplementedException();
        }

        public static UserAction WaitForLabelValue(List<string> containers, string expectedValue, int timeOutInSeconds, string container = null)
        {
            DateTime startTime = DateTime.Now;
            DateTime currentTime = DateTime.Now;
            UserAction UserAction = null;
            while (currentTime.Subtract(startTime).TotalSeconds < timeOutInSeconds &&
                   (UserAction = VerifyLabelValue(containers, expectedValue)).getResult() != ExecutionResult.Pass)
            {
                currentTime = DateTime.Now;
            }
            if (UserAction == null || UserAction.getResult() != ExecutionResult.Pass)
            {
                return new UserAction(ExecutionResult.Fail, $"The label value was not found after {timeOutInSeconds} seconds");
            }
            return UserAction;
        }
        public static UserAction VerifyButtonStatus(List<string> containersList, string status)
        {
            try
            {
                var res = FindPathToObject(LookupArea, containersList);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement pathToButton = res.getValue();
                if (pathToButton != null)
                {
                    if ((pathToButton.Enabled) && (status == "enabled"))
                    {

                        return new UserAction(ExecutionResult.Pass, $"The button: {containersList.Last()} is as expected status: {status}");
                    }
                    else
                    {
                        var attribute = pathToButton.GetAttribute("disabled");
                        if (attribute == null)
                        {
                            attribute = pathToButton.GetAttribute("data-disabled");
                        }
                        if ((attribute == "true") && (status == "disabled"))
                        {
                            return new UserAction(ExecutionResult.Pass, $"The button: {containersList.Last()} is  as expected status: {status}");
                        }
                        return new UserAction(ExecutionResult.Warning, $"The button: {containersList.Last()} is not as expected status: {status}");
                    }
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, "Button not found");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, e.Message);
            }
        }
        public static UserAction RemoveFocus(string container, string innerElement)
        {
            string action = "RemoveFocus(" + container + "," + innerElement + ")";
            try
            {
                var res = FindPathToObjectUnderContainer(container, innerElement);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement element = res.getValue();
                new Actions(driver).Click().Perform();
                return new UserAction(ExecutionResult.Pass, $"Remove focus from {innerElement} successfully");
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, $"Fail to remove focuse from {innerElement}: {e.Message}");
            }
        }

        public static UserAction VerifyLabelValue(List<string> containers, string expectedValue, string ignoreChar = "")
        {
            string action = $"VerifyLabelValue({containers},{expectedValue})";
            UserAction getLabelValueResult;
            try
            {
                getLabelValueResult = GetLabelValue(containers);
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, $"Could not get the label text: {e.Message}");
            }
            if (getLabelValueResult.getResult() != ExecutionResult.Pass)
            {

                return new UserAction(ExecutionResult.Fail, $"Failed to get label value:{getLabelValueResult.details}");
            }
            string currentValue = getLabelValueResult.getValue();
            if (ignoreChar != "")
                currentValue = currentValue.Replace(ignoreChar, "");
            Reporter(action, "Info", $"Label found, value: {currentValue}, Comparing the label value to { expectedValue})");
            if (currentValue.Equals(expectedValue, StringComparison.InvariantCultureIgnoreCase))
            {
                return new UserAction(ExecutionResult.Pass, $"The label value is {currentValue}, as expected: {expectedValue}");
            }
            else
            {
                return new UserAction(ExecutionResult.Fail, $"The label value is {currentValue}, not as expected: {expectedValue}");
            }
        }
        public static UserAction VerifyLabelExistence(string strLabelObject, string existence)
        {
            string action = $"VerifyLabelExistence({strLabelObject},{existence})";
            try
            {
                (var res, bool hasValue) = FindPathToObject(LookupArea, strLabelObject).tryGetValue();

                if (!hasValue && existence.Equals(ExistenceStatus.not_exist.ToString(), StringComparison.InvariantCultureIgnoreCase))
                {
                    return new UserAction(ExecutionResult.Pass,
                        $"The label {strLabelObject} was not found, as expected");
                }
                else if (hasValue && existence.Equals(ExistenceStatus.exist.ToString(), StringComparison.InvariantCultureIgnoreCase))
                {
                    return new UserAction(ExecutionResult.Pass,
                        $"The label {strLabelObject} has been found, as expected");
                }
                else if (hasValue && existence.Equals(ExistenceStatus.exist.ToString(), StringComparison.InvariantCultureIgnoreCase))
                {
                    return new UserAction(ExecutionResult.Warning,
                        $"The label {strLabelObject} was not found");
                }
                else
                {
                    return new UserAction(ExecutionResult.Warning,
                        $"The label {strLabelObject} was found, not as expected");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, e.Message);
            }
        }
        public static UserAction GetCountOfInnerDivs(string wrappingDivId)
        {
            try
            {
                var res = FindPathToObject(LookupArea, wrappingDivId);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement pathToDiv = res.getValue();

                IReadOnlyCollection<IWebElement> innerElements = pathToDiv.FindElements(By.TagName("div"));
                if (innerElements == null)
                {
                    return new UserAction(ExecutionResult.Fail, $"Failed to find inner div elements within {wrappingDivId}");
                }
                int count = innerElements.Count;
                return new UserAction(ExecutionResult.Pass, $"Found {count} inner div elements under {wrappingDivId}", count.ToString());
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, e.Message);
            }
        }
        public static UserAction NavigateToPreviousPage()
        {
            throw new NotImplementedException();
        }

        public static UserAction SetBrowserUrl(string URL)
        {
            string action = $"SetBrowserUrl({URL})";
            try
            {
                if (driver != null)
                {
                    if (driver.Url.Equals(URL, StringComparison.OrdinalIgnoreCase))
                    {
                        return new UserAction(ExecutionResult.Pass, $"Browser URL is already:{URL}");
                    }
                    else
                    {
                        driver.Url = URL;
                    }
                }
                return new UserAction(ExecutionResult.Pass, $"Browser URL was set to:{URL}");
            }
            catch (System.Exception ex)
            {
                return new UserAction(ExecutionResult.Fail, ex.Message);
            }
        }

        public static UserAction VerifyWindow(string strWindow)
        {
            throw new NotImplementedException();
        }

        public static UserAction WaitForWindow(string strWindow, string timeout)
        {
            throw new NotImplementedException();
        }

        public static UserAction SelectRowInTable(string strTableName, string strColumn, string strValue)
        {
            throw new NotImplementedException();
        }

        public static UserAction DoubleClickOnButton(string strButtonName)
        {
            throw new NotImplementedException();
        }

        public static void findByName(string name)
        {
            List<IWebElement> objectsList = driver.FindElements(By.Name(name)).ToList();
            Reporter("findByName", "Info", $"found {objectsList.Count()} elements");
        }

        public static UserAction SetFieldValue(string strName, string strValue, string clickEnter = "false")
        {
            try
            {
                var res = FindPathToObject(LookupArea, strName);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement pathToTextField = res.getValue();
                if ((pathToTextField == null))
                {
                    return new UserAction(ExecutionResult.Fail, $"The text field wasn't found!");
                }
                pathToTextField.Click();
                Thread.Sleep(100);
                pathToTextField.Clear();
                Thread.Sleep(100);
                pathToTextField.SendKeys(strValue);
                Thread.Sleep(1000);
                if (pathToTextField.GetAttribute("value") == strValue || Convert.ToInt32(pathToTextField.GetAttribute("value")) == Convert.ToInt32(strValue))
                {
                    if (Boolean.Parse(clickEnter))
                        pathToTextField.SendKeys(Keys.Return);
                    return new UserAction(ExecutionResult.Pass, $"The value {strValue} was set correctly");
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, $"The value was not set correctly - expected: {strValue}, actual: {pathToTextField.Text}");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, $"{e.Message}");
            }
        }


        public static UserAction SelectFromDropDownList(string strListName, string strItem)
        {
            var Action = "SelectFromDropDownList(" + strListName + "," + strItem + ")";

            // Check for sub item
            //var strSubItem = "";
            //if (Strings.InStr(1, strItem, ";") > 0)
            //{
            //    strSubItem = Strings.Split(strItem, ">")(1);      
            //    strItem = Strings.Split(strItem, ">")(0);
            //}

            try
            {
                var res = FindPathToObject(LookupArea, strListName);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement PathToComboBox = res.getValue();
                if (PathToComboBox != null)
                {
                    if (PathToComboBox.Enabled)
                    {
                        PathToComboBox.Click();
                        Reporter(Action, "Info", "ComboBox found and clicked");
                        Thread.Sleep(1000);
                        IEnumerable<IWebElement> MenuOptions = PathToComboBox.FindElements(By.XPath(".//*[contains(@class,'menuitem'|'dropdown-item')]")).Where(x => x.Text == strItem);
                        if (MenuOptions.Count() == 0)
                            MenuOptions = PathToComboBox.FindElements(By.TagName("md-option")).Where(x => x.Text == strItem);
                        Reporter(Action, "Info", "Found " + MenuOptions.Count() + " menuitems with text:" + strItem);
                        if (MenuOptions.Count() > 0)
                        {
                            IWebElement SelectedValue = MenuOptions.FirstOrDefault();
                            //if (strSubItem == "") 
                            //{

                            // No sub item
                            SelectedValue.Click();
                            //}
                            //else
                            //{
                            //    // With sub item 
                            //    Actions Actions = new Actions(driver);
                            //    Actions.MoveToElement(SelectedValue, 5, 5).Build().Perform();
                            //    MenuOptions = PathToComboBox.FindElements(By.XPath(".//*[contains(@class,'menuitem'|'dropdown-item')]")).Where(x => x.Text == strSubItem);
                            //    if (MenuOptions.Count() == 0)
                            //        MenuOptions = PathToComboBox.FindElements(By.TagName("md-option")).Where(x => x.Text == strSubItem);
                            //    SelectedValue = MenuOptions.FirstOrDefault();
                            //    SelectedValue.Click();
                            //}
                            return new UserAction(ExecutionResult.Pass, "Drop down list item was selected");
                        }
                        else
                            return new UserAction(ExecutionResult.Fail, "Drop down list item " + strItem + " wasn't found!");
                    }
                    else
                        return new UserAction(ExecutionResult.Fail, "ComboBox is disabled");
                }
                else
                    return new UserAction(ExecutionResult.Fail, "ComboBox not found");
            }
            // Dim MenuOptions = driver.FindElements(By.ClassName("dropdown_dark_menuitem_txt") Or By.ClassName("dropdown_btn_menuitem")).Where(Function(x) x.Text = strItem)
            catch (System.Exception ex)
            {
                return new UserAction(ExecutionResult.Fail, ex.Message);
            }
        }

        public static UserAction<bool> IsDriverAlive()
        {
            if (driver != null)
            {
                try
                {
                    bool result = driver.WindowHandles != null; //returns true if browser instance exist or throws an exception
                    return new UserAction<bool>(ExecutionResult.Pass, $"WindowHandles found?{result}", result);
                }
                catch (System.Exception ex)
                {
                    //Browser closed by user
                    return new UserAction<bool>(ExecutionResult.Pass, $"get driver window handles threw exception", false);
                }
            }
            return new UserAction<bool>(ExecutionResult.Pass, $"driver is null", false);
        }

        public static UserAction SelectRowInTableByIndex(string strTableName, string intIndex)
        {
            throw new NotImplementedException();
        }

        public static UserAction VerifyFieldValue(string container, string strFieldName, string strExpValue, int index = 1)
        {
            string action = "VerifyFieldValue(" + container + "," + strFieldName + ")";
            var res = FindPathToObjectUnderContainer(container, strFieldName, index);
            if (res.getResult() != ExecutionResult.Pass)
            {
                return new UserAction(ExecutionResult.Fail, res.details);
            }

            IWebElement innerElement = res.getValue();

            if (innerElement.GetAttribute("value") == strExpValue)
            {
                return new UserAction(ExecutionResult.Pass, "the value is as expected");
            }
            else
            {
                return new UserAction(ExecutionResult.Fail, $"the value is not as expected value{innerElement.GetAttribute("value")} strExpValue:{strExpValue}");
            }
        }






        public static UserAction SetCheckBoxValue(List<string> objContainersList, string strValueToSet)
        {
            try
            {
                var res = FindPathToObject(LookupArea, objContainersList);
                if (res.getResult() != ExecutionResult.Pass)
                {
                    return new UserAction(ExecutionResult.Fail, res.details);
                }
                IWebElement checkBoxElement = res.getValue();


                string objectName = objContainersList[objContainersList.Count - 1];
                if (strValueToSet == "ON")
                {
                    if (checkBoxElement.Selected)
                    {
                        return new UserAction(ExecutionResult.Pass, $"The checkbox: {objectName} is already: {strValueToSet}");
                    }
                    else
                    {
                        checkBoxElement.Click();
                        return new UserAction(ExecutionResult.Pass, $"The checkbox: {objectName} set to: {strValueToSet}");
                    }
                }
                else if (strValueToSet == "OFF")
                {
                    if (checkBoxElement.Selected)
                    {
                        checkBoxElement.Click();
                        return new UserAction(ExecutionResult.Pass, $"The checkbox: {objectName} set to: {strValueToSet}");
                    }
                    else
                    {
                        return new UserAction(ExecutionResult.Pass, $"The checkbox: {objectName} is already: {strValueToSet}");
                    }
                }
                else
                {
                    return new UserAction(ExecutionResult.Fail, $"Failed to set checkbox: {objectName} to {strValueToSet} - unknown state");
                }
            }
            catch (System.Exception e)
            {
                return new UserAction(ExecutionResult.Fail, e.Message);
            }
        }

        public static UserAction VerifyCheckBoxValue(string strCheckBoxName, string strExpValue)
        {
            throw new NotImplementedException();
        }
        public static UserAction ClickAndSetFieldValue(string strButtonName, string strFieldName, string strValue)
        {
            if (ClickOnButton(strButtonName).result == "true" &&
             SetFieldValue(strFieldName, strValue).result == "true")

                return new UserAction(ExecutionResult.Pass, $"The value {strValue} was set correctly");
            else

                return new UserAction(ExecutionResult.Fail, $"The value {strValue} was set correctly");
        }

        public static UserAction SelectFromPopupMenu(string strTableName, string strRow, string strMenu)
        {
            throw new NotImplementedException();
        }


        public static UserAction VerifyTableCellValue(string strTableName, string strRow, string strColumn, string strExpValue)
        {
            throw new NotImplementedException();
        }

    }
}
