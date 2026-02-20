namespace AtomixAI.Core
{
    public interface ITransactionHandler : IRevitContext, IDisposable
    {
        void Assimilate();
        void Rollback();
        //Autodesk.Revit.DB.Document Document { get; } // Добавьте это 
    }
}