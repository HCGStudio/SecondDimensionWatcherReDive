import React from "react";
import { Navigate } from "react-router-dom";
import { useLoginStatus } from "../auth/hooks";

export const ProtectedRoute: React.FC<React.PropsWithChildren> = ({
  children,
}) => {
  const { data: status, error } = useLoginStatus();

  // Still loading
  if (!status && !error) return null;

  // Not authenticated
  if (error || !status) return <Navigate to="/login" replace />;

  return <>{children}</>;
};
