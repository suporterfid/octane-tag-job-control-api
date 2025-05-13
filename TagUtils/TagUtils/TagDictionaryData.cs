using Impinj;


namespace Impinj.TagUtils
{
    public class TagDictionaryData
    {
        public string TagTid = string.Empty;
        public string EpcValue = string.Empty;
        public Sgtin96 TargetSgtin96Value = null;
        public string PcValue = string.Empty;
        public string AccessPwValue = string.Empty;
        public CompletionCriteriaEnum completionCriteria = CompletionCriteriaEnum.None;
        public bool EpcEncodeSuccessful = false;
        public bool AccessPwEncodeSuccessful = false;
        public bool PCEncodeSuccessful = false;

        public bool EncodeOperationsComplete
        {
            get
            {
                switch (completionCriteria)
                {
                    case CompletionCriteriaEnum.EPC:
                        return EpcEncodeSuccessful;
                    case CompletionCriteriaEnum.PC:
                        return PCEncodeSuccessful;
                    case CompletionCriteriaEnum.EPC_And_PC:
                        return EpcEncodeSuccessful & PCEncodeSuccessful;
                    case CompletionCriteriaEnum.AccessPW:
                        return AccessPwEncodeSuccessful;
                    case CompletionCriteriaEnum.EPC_And_AccessPW:
                        return EpcEncodeSuccessful & AccessPwEncodeSuccessful;
                    case CompletionCriteriaEnum.PC_And_AccessPW:
                        return PCEncodeSuccessful & AccessPwEncodeSuccessful;
                    case CompletionCriteriaEnum.EPC_And_PC_And_AccessPW:
                        return EpcEncodeSuccessful & PCEncodeSuccessful & AccessPwEncodeSuccessful;
                    default:
                        return true;
                }
            }
        }

        public TagDictionaryData()
        {
        }

        public TagDictionaryData(
          string Tid,
          CompletionCriteriaEnum completion_criteria,
          string Epc,
          Sgtin96 TargetSgtin96 = null,
          string Pc = null,
          string Access_Pw = null)
        {
            TagTid = Tid;
            EpcValue = Epc;
            TargetSgtin96Value = TargetSgtin96;
            PcValue = Pc;
            AccessPwValue = Access_Pw;
            completionCriteria = completion_criteria;
        }
    }
}
