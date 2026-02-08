using System;
using System.Collections.Generic;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Minimal result type for build/validation operations.
    /// </summary>
    public readonly struct Result<T>
    {
        private readonly T _value;
        private readonly IReadOnlyList<ValidationError> _errors;

        public bool IsSuccess { get; }

        public bool IsFailure => !IsSuccess;

        public T Value => IsSuccess
            ? _value
            : throw new InvalidOperationException("Cannot access Value when result is failure.");

        public IReadOnlyList<ValidationError> Errors => IsSuccess
            ? Array.Empty<ValidationError>()
            : _errors ?? Array.Empty<ValidationError>();

        private Result(T value)
        {
            IsSuccess = true;
            _value = value;
            _errors = Array.Empty<ValidationError>();
        }

        private Result(IReadOnlyList<ValidationError> errors)
        {
            IsSuccess = false;
            _value = default;
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public static Result<T> Success(T value) => new(value);

        public static Result<T> Failure(IReadOnlyList<ValidationError> errors)
        {
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));

            return errors.Count == 0 
                ? throw new ArgumentException("Failure must contain at least one error.", nameof(errors)) 
                : new Result<T>(errors);
        }

        public static Result<T> Failure(params ValidationError[] errors)
        {
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));

            return errors.Length == 0 
                ? throw new ArgumentException("Failure must contain at least one error.", nameof(errors)) 
                : new Result<T>(errors);
        }
    }
}