using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Data;
using AutomationTestsCore;
using CrossPlatformsUserActionsWrappers;
using TrinityUserActionsWrappers;
using S6UserActionsWrappers;
using S6General.Utils;

namespace $rootnamespace$
{
    /// <summary>
    /// 
    /// </summary>
    /// <RelevantProjects> </RelevantProjects>
    /// <RequiredHardware> </RequiredHardware>
	/// <Author> </Author>
    /// <parameters></parameters> 
    public abstract class $safeitemname$ : AutomationTest
    {
        #region Action and Rollback Definitions

        public enum RollBackSteps
        {
            //enter rollbackSteps divided by ',' for example:
            //CloseBidEngageScreen, ReturnToStanBy
        }

        public $safeitemname$()
        {
            // add the mapping between steps to rollback functions, for example:
            // rollback.AddRollbackStep(new object(), () => { });
        }

        #endregion
        #region Main Flow
        #endregion
        #region Functions
        #endregion
		#region Rollback
        #endregion 
    }
}
