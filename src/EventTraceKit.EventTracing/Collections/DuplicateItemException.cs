namespace EventTraceKit.EventTracing.Collections
{
    using System;
    using System.Runtime.Serialization;
    using System.Security;

    /// <summary>Duplicate entry.</summary>
    [Serializable]
    public sealed class DuplicateItemException : Exception
    {
        /// <overloads>
        ///   Initializes a new instance of the <see cref="DuplicateItemException"/>
        ///   class.
        /// </overloads>
        /// <summary>
        ///   Initializes a new instance of the <see cref="DuplicateItemException"/>
        ///   class with default properties.
        /// </summary>
        public DuplicateItemException()
        {
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="DuplicateItemException"/>
        ///   class with a specified error message.
        /// </summary>
        /// <param name="message">
        ///   The error message that explains the reason for this exception.
        /// </param>
        public DuplicateItemException(string message)
            : base(message)
        {
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="DuplicateItemException"/>
        ///   class with a specified error message and the exception that is the
        ///   cause of this exception.
        /// </summary>
        /// <param name="message">
        ///   The error message that explains the reason for this exception.
        /// </param>
        /// <param name="innerException">
        ///   The exception that is the cause of the current exception, or a
        ///   <see langword="null"/> reference (<c>Nothing</c> in Visual Basic)
        ///   if no inner exception is specified.
        /// </param>
        public DuplicateItemException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="DuplicateItemException"/>
        ///   class with serialized data.
        /// </summary>
        /// <param name="info">
        ///   The serialization information object holding the serialized object
        ///   data in the name-value form.
        /// </param>
        /// <param name="context">
        ///   The contextual information about the source or destination of the
        ///   exception.
        /// </param>
        private DuplicateItemException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        ///   When overridden in a derived class, sets the <see cref="SerializationInfo"/>
        ///   with information about the exception.
        /// </summary>
        /// <param name="info">
        ///   The <see cref="SerializationInfo"/> that holds the serialized object
        ///   data about the exception being thrown.
        /// </param>
        /// <param name="context">
        ///   The <see cref="StreamingContext"/> that contains contextual information
        ///   about the source or destination.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///   <paramref name="info"/> is <see langword="null"/>.
        /// </exception>
        [SecurityCritical]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            base.GetObjectData(info, context);
        }
    }
}
